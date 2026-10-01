using Hermes.Interfaces;

namespace Hermes;

/*
    var addCommand = new UndoableCommand(
        () => Items.Add(newItem),
        () => Items.Remove(newItem));

    addCommand.Execute(null);   // Adds item
    addCommand.Undo();          // Removes item

    // Shared history with Undo/Redo commands and grouping:
    var history = new UndoRedoManager(100);
    addCommand.WithUndoManager(history);   // Execute now records into 'history'
 */

/// <summary>
/// Command with undo support. Standalone it remembers only the last execution;
/// with <see cref="WithUndoManager"/> every execution is recorded in a shared <see cref="UndoRedoManager"/>
/// (multi-level undo/redo).
/// </summary>
public class UndoableCommand : DelegateCommandBase
{
    private readonly Action _executeMethod;
    private readonly Action _undoMethod;
    private Func<bool> _canExecuteMethod;
    private Func<bool>? _canUndoMethod;
    private UndoRedoManager? _manager;
    private bool _hasExecuted;

    public UndoableCommand(Action executeMethod, Action undoMethod)
        : this(executeMethod, undoMethod, () => true)
    {
    }

    public UndoableCommand(Action executeMethod, Action undoMethod, Func<bool> canExecuteMethod)
    {
        _executeMethod = executeMethod ?? throw new ArgumentNullException(nameof(executeMethod));
        _undoMethod = undoMethod ?? throw new ArgumentNullException(nameof(undoMethod));
        _canExecuteMethod = canExecuteMethod ?? throw new ArgumentNullException(nameof(canExecuteMethod));
    }

    /// <summary>Gets whether the command has been executed and not undone (standalone mode).</summary>
    public bool HasExecuted
    {
        get => _hasExecuted;
        private set
        {
            if (_hasExecuted == value) return;
            _hasExecuted = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanUndoState));
        }
    }

    /// <summary>Bindable version of <see cref="CanUndo"/>.</summary>
    public bool CanUndoState => CanUndo();

    public bool CanUndo() => _manager != null ? _manager.CanUndo : _hasExecuted && (_canUndoMethod?.Invoke() ?? true);

    /// <summary>Undoes the last execution (or the last step of the shared history).</summary>
    public void Undo()
    {
        if (!CanUndo()) return;

        try
        {
            Log("Undoing command");
            if (_manager != null)
            {
                _manager.Undo();
            }
            else
            {
                _undoMethod();
                HasExecuted = false;
            }
            RaiseCanExecuteChanged();
        }
        catch (Exception ex)
        {
            if (!HandleException(ex))
                throw;
        }
    }

    protected override void Execute(object? parameter)
    {
        try
        {
            Log("Executing undoable command");
            if (_manager != null)
                _manager.Execute(new DelegateOperation(_executeMethod, _undoMethod));
            else
            {
                _executeMethod();
                HasExecuted = true;
            }
            RaiseCanExecuteChanged();
        }
        catch (Exception ex)
        {
            if (!HandleException(ex))
                throw;
        }
    }

    protected override bool CanExecute(object? parameter) => _canExecuteMethod();

    #region Fluent API

    public UndoableCommand When(Func<bool> canExecuteMethod) { _canExecuteMethod = canExecuteMethod; return this; }
    public UndoableCommand WhenUndo(Func<bool> canUndoMethod) { _canUndoMethod = canUndoMethod; return this; }

    /// <summary>Records every execution in a shared undo/redo history.</summary>
    public UndoableCommand WithUndoManager(UndoRedoManager manager) { _manager = manager; return this; }

    public static UndoableCommand Create(Action execute, Action undo) => new(execute, undo);

    #endregion
}

/// <summary>
/// Undoable command with a typed parameter. Standalone it stores the last parameter for Undo.
/// </summary>
public class UndoableCommand<T> : DelegateCommandBase
{
    private readonly Action<T> _executeMethod;
    private readonly Action<T> _undoMethod;
    private Func<T, bool> _canExecuteMethod;
    private Func<T, bool>? _canUndoMethod;
    private UndoRedoManager? _manager;
    private bool _hasExecuted;
    private T? _lastParameter;

    public UndoableCommand(Action<T> executeMethod, Action<T> undoMethod)
        : this(executeMethod, undoMethod, _ => true)
    {
    }

    public UndoableCommand(Action<T> executeMethod, Action<T> undoMethod, Func<T, bool> canExecuteMethod)
    {
        _executeMethod = executeMethod ?? throw new ArgumentNullException(nameof(executeMethod));
        _undoMethod = undoMethod ?? throw new ArgumentNullException(nameof(undoMethod));
        _canExecuteMethod = canExecuteMethod ?? throw new ArgumentNullException(nameof(canExecuteMethod));
    }

    public bool HasExecuted
    {
        get => _hasExecuted;
        private set
        {
            if (_hasExecuted == value) return;
            _hasExecuted = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanUndoState));
        }
    }

    public bool CanUndoState => CanUndo();

    public bool CanUndo()
        => _manager != null
            ? _manager.CanUndo
            : _hasExecuted && (_canUndoMethod?.Invoke(_lastParameter!) ?? true);

    public void Undo()
    {
        if (!CanUndo()) return;

        try
        {
            Log("Undoing command");
            if (_manager != null)
            {
                _manager.Undo();
            }
            else
            {
                _undoMethod(_lastParameter!);
                HasExecuted = false;
            }
            RaiseCanExecuteChanged();
        }
        catch (Exception ex)
        {
            if (!HandleException(ex))
                throw;
        }
    }

    protected override void Execute(object? parameter)
    {
        var typed = CommandParameter.Cast<T>(parameter);
        try
        {
            Log("Executing undoable command");
            if (_manager != null)
            {
                _manager.Execute(new DelegateOperation(() => _executeMethod(typed), () => _undoMethod(typed)));
            }
            else
            {
                _executeMethod(typed);
                _lastParameter = typed;
                HasExecuted = true;
            }
            RaiseCanExecuteChanged();
        }
        catch (Exception ex)
        {
            if (!HandleException(ex))
                throw;
        }
    }

    protected override bool CanExecute(object? parameter)
        => CommandParameter.TryCast<T>(parameter, out var typed) && _canExecuteMethod(typed);

    #region Fluent API

    public UndoableCommand<T> When(Func<T, bool> canExecuteMethod) { _canExecuteMethod = canExecuteMethod; return this; }
    public UndoableCommand<T> WhenUndo(Func<T, bool> canUndoMethod) { _canUndoMethod = canUndoMethod; return this; }
    public UndoableCommand<T> WithUndoManager(UndoRedoManager manager) { _manager = manager; return this; }
    public static UndoableCommand<T> Create(Action<T> execute, Action<T> undo) => new(execute, undo);

    #endregion
}
