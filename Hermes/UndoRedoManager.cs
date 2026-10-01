using Hermes.Interfaces;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace Hermes;

/*
    var history = new UndoRedoManager(capacity: 100);

    history.Execute(new DelegateOperation(
        () => Items.Add(item), () => Items.Remove(item), "Add item"));

    using (history.BeginGroup("Move and rename"))
    {
        history.Execute(moveOperation);
        history.Execute(renameOperation);
    }                                   // one Undo reverts both

    // XAML: Command="{Binding History.UndoCommand}" / "{Binding History.RedoCommand}"

    // Link commands to the shared history:
    AddCommand = new UndoableCommand(() => Items.Add(item), () => Items.Remove(item))
        .WithUndoManager(history);
 */

/// <summary>Operation built from two delegates.</summary>
public sealed class DelegateOperation : IUndoableOperation
{
    private readonly Action _execute;
    private readonly Action _undo;

    public DelegateOperation(Action execute, Action undo, string? description = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _undo = undo ?? throw new ArgumentNullException(nameof(undo));
        Description = description;
    }

    public string? Description { get; }

    public void Execute() => _execute();

    public void Undo() => _undo();
}

/// <summary>
/// Undo/redo history with capacity limit, grouping (transactions) and bindable Undo/Redo commands.
/// Not thread-safe: use it from the UI thread.
/// </summary>
public sealed class UndoRedoManager : INotifyPropertyChanged
{
    private readonly LinkedList<IUndoableOperation> _undo = new();
    private readonly Stack<IUndoableOperation> _redo = new();
    private readonly Stack<(string? Description, List<IUndoableOperation> Operations)> _groups = new();
    private DelegateCommand? _undoCommand;
    private DelegateCommand? _redoCommand;

    /// <param name="capacity">Maximum number of undo steps; 0 or less = unlimited.</param>
    public UndoRedoManager(int capacity = 0) => Capacity = capacity;

    public int Capacity { get; }

    public bool CanUndo => _groups.Count == 0 && _undo.Count > 0;

    public bool CanRedo => _groups.Count == 0 && _redo.Count > 0;

    public string? UndoDescription => _undo.Last?.Value.Description;

    public string? RedoDescription => _redo.Count > 0 ? _redo.Peek().Description : null;

    public int UndoCount => _undo.Count;

    public int RedoCount => _redo.Count;

    public ICommand UndoCommand => _undoCommand ??= new DelegateCommand(Undo, () => CanUndo);

    public ICommand RedoCommand => _redoCommand ??= new DelegateCommand(Redo, () => CanRedo);

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raised after any change of the history.</summary>
    public event EventHandler? HistoryChanged;

    /// <summary>Executes the operation and records it (clears the redo stack).</summary>
    public void Execute(IUndoableOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        operation.Execute();

        if (_groups.Count > 0)
        {
            _groups.Peek().Operations.Add(operation);
            return;
        }

        Push(operation);
    }

    /// <summary>Records an operation that was already performed (without executing it).</summary>
    public void Record(IUndoableOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        if (_groups.Count > 0)
            _groups.Peek().Operations.Add(operation);
        else
            Push(operation);
    }

    public void Undo()
    {
        if (!CanUndo) return;

        var operation = _undo.Last!.Value;
        operation.Undo();
        _undo.RemoveLast();
        _redo.Push(operation);
        Changed();
    }

    public void Redo()
    {
        if (!CanRedo) return;

        var operation = _redo.Peek();
        operation.Execute();
        _redo.Pop();
        _undo.AddLast(operation);
        TrimCapacity();
        Changed();
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        Changed();
    }

    /// <summary>
    /// Starts a group: all operations executed until the returned scope is disposed
    /// are recorded as a single undo step.
    /// </summary>
    public IDisposable BeginGroup(string? description = null)
    {
        _groups.Push((description, new List<IUndoableOperation>()));
        Changed();
        return new GroupScope(this);
    }

    private void EndGroup()
    {
        var (description, operations) = _groups.Pop();
        if (operations.Count > 0)
        {
            IUndoableOperation group = operations.Count == 1
                ? operations[0]
                : new GroupOperation(operations, description);

            if (_groups.Count > 0)
                _groups.Peek().Operations.Add(group);
            else
                Push(group);
        }
        else
        {
            Changed();
        }
    }

    private void Push(IUndoableOperation operation)
    {
        _undo.AddLast(operation);
        _redo.Clear();
        TrimCapacity();
        Changed();
    }

    private void TrimCapacity()
    {
        if (Capacity <= 0) return;
        while (_undo.Count > Capacity)
            _undo.RemoveFirst();
    }

    private void Changed()
    {
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        OnPropertyChanged(nameof(UndoDescription));
        OnPropertyChanged(nameof(RedoDescription));
        OnPropertyChanged(nameof(UndoCount));
        OnPropertyChanged(nameof(RedoCount));
        _undoCommand?.RaiseCanExecuteChanged();
        _redoCommand?.RaiseCanExecuteChanged();
        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private sealed class GroupScope : IDisposable
    {
        private UndoRedoManager? _owner;
        public GroupScope(UndoRedoManager owner) => _owner = owner;

        public void Dispose()
        {
            _owner?.EndGroup();
            _owner = null;
        }
    }

    private sealed class GroupOperation : IUndoableOperation
    {
        private readonly List<IUndoableOperation> _operations;

        public GroupOperation(List<IUndoableOperation> operations, string? description)
        {
            _operations = operations;
            Description = description;
        }

        public string? Description { get; }

        public void Execute()
        {
            foreach (var operation in _operations)
                operation.Execute();
        }

        public void Undo()
        {
            for (int i = _operations.Count - 1; i >= 0; i--)
                _operations[i].Undo();
        }
    }
}
