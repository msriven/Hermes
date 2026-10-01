using System.Windows.Input;

namespace Hermes.Interfaces;

/// <summary>
/// Command that can be awaited and cancelled. Implemented by all Hermes async commands
/// so that composite commands can wait for their completion.
/// </summary>
public interface IAsyncCommand : ICommand
{
    /// <summary>Gets a value indicating whether at least one execution is in progress.</summary>
    bool IsExecuting { get; }

    /// <summary>Executes the command and returns a task that completes when execution finishes.</summary>
    Task ExecuteAsync(object? parameter, CancellationToken cancellationToken = default);

    /// <summary>Cancels all running executions.</summary>
    void Cancel();
}
