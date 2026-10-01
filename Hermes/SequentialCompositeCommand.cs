using System.Collections.ObjectModel;
using System.Runtime.ExceptionServices;
using System.Windows.Input;

namespace Hermes;

/*
    var pipeline = new SequentialCompositeCommand(stopOnFailure: true)
        .OnError((cmd, ex) => Debug.WriteLine($"Command failed: {ex.Message}"));
    pipeline.RegisterCommand(validateCommand);
    pipeline.RegisterCommand(saveCommand);

    // NOTE: children are executed synchronously. For async children use AsyncSequentialCommand.
 */

/// <summary>
/// Executes registered commands one after another (synchronously).
/// stopOnFailure = true: CanExecute requires ALL children, execution stops at the first failure or
/// non-executable child. stopOnFailure = false: CanExecute requires ANY child, failing/non-executable
/// children are skipped and execution continues.
/// If no error handler is configured (OnError / CatchExceptions / GlobalExceptionHandler) exceptions are
/// no longer swallowed: they are re-thrown (immediately when stopping, aggregated at the end otherwise).
/// </summary>
public class SequentialCompositeCommand : DelegateCommandBase
{
    private readonly CommandCollection _commands;
    private readonly bool _stopOnFailure;
    private Action<ICommand, Exception>? _errorHandler;

    public SequentialCompositeCommand(bool stopOnFailure = true)
    {
        _stopOnFailure = stopOnFailure;
        _commands = new CommandCollection(OnCanExecuteChanged);
    }

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
            if (!command.CanExecute(parameter))
            {
                if (_stopOnFailure) break;
                continue;
            }

            try
            {
                command.Execute(parameter);
            }
            catch (Exception ex)
            {
                bool handled;
                if (_errorHandler != null)
                {
                    _errorHandler(command, ex);
                    handled = true;
                }
                else
                {
                    handled = HandleException(ex);
                }

                if (!handled)
                {
                    if (_stopOnFailure)
                        ExceptionDispatchInfo.Capture(ex).Throw();
                    (unhandled ??= new List<Exception>()).Add(ex);
                }

                if (_stopOnFailure) break;
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

        return _stopOnFailure
            ? snapshot.All(c => c.CanExecute(parameter))
            : snapshot.Any(c => c.CanExecute(parameter));
    }

    /// <summary>Sets a per-command error handler.</summary>
    public SequentialCompositeCommand OnError(Action<ICommand, Exception> errorHandler)
    {
        _errorHandler = errorHandler;
        return this;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _commands.Clear();
        base.Dispose(disposing);
    }
}
