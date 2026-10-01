using System.Collections.ObjectModel;
using System.Runtime.ExceptionServices;
using System.Windows.Input;

namespace Hermes;

/// <summary>
/// How a composite command combines CanExecute of its children.
/// </summary>
public enum CanExecuteMode
{
    /// <summary>Executable if ANY child can execute.</summary>
    Any,

    /// <summary>Executable only if ALL children can execute.</summary>
    All
}

/// <summary>
/// Composite command that executes all registered commands that can execute.
/// A failing child no longer prevents the remaining ones from running: errors are passed to the
/// exception handler (CatchExceptions) or, if none is set, re-thrown after all children ran
/// (AggregateException when there are several).
/// </summary>
public class CompositeCommand : DelegateCommandBase
{
    private readonly CommandCollection _commands;
    private readonly CanExecuteMode _mode;

    /// <param name="monitorCommandActivity">
    /// Kept for compatibility: false = <see cref="CanExecuteMode.Any"/>, true = <see cref="CanExecuteMode.All"/>.
    /// Prefer the <see cref="CanExecuteMode"/> overload; use <see cref="RespectIsActive"/> to honor IActiveAware.
    /// </param>
    public CompositeCommand(bool monitorCommandActivity = false)
        : this(monitorCommandActivity ? CanExecuteMode.All : CanExecuteMode.Any)
    {
    }

    public CompositeCommand(CanExecuteMode mode)
    {
        _mode = mode;
        _commands = new CommandCollection(OnCanExecuteChanged);
    }

    /// <summary>
    /// When true, children implementing IActiveAware participate only while IsActive is true.
    /// </summary>
    public bool RespectIsActive
    {
        get => _commands.RespectIsActive;
        set
        {
            if (_commands.RespectIsActive == value) return;
            _commands.RespectIsActive = value;
            OnPropertyChanged();
            OnCanExecuteChanged();
        }
    }

    public ReadOnlyCollection<ICommand> RegisteredCommands => _commands.All();

    public void RegisterCommand(ICommand command) => _commands.Add(command);

    public void UnregisterCommand(ICommand command) => _commands.Remove(command);

    protected override void Execute(object? parameter)
    {
        List<Exception>? unhandled = null;

        foreach (var command in _commands.Snapshot())
        {
            if (!command.CanExecute(parameter)) continue;

            try
            {
                command.Execute(parameter);
            }
            catch (Exception ex)
            {
                if (!HandleException(ex))
                    (unhandled ??= new List<Exception>()).Add(ex);
            }
        }

        if (unhandled is { Count: 1 })
            ExceptionDispatchInfo.Capture(unhandled[0]).Throw();
        if (unhandled is { Count: > 1 })
            throw new AggregateException(unhandled);
    }

    protected override bool CanExecute(object? parameter)
    {
        var snapshot = _commands.Snapshot();
        if (snapshot.Count == 0) return false;

        return _mode == CanExecuteMode.All
            ? snapshot.All(c => c.CanExecute(parameter))
            : snapshot.Any(c => c.CanExecute(parameter));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _commands.Clear();
        base.Dispose(disposing);
    }
}
