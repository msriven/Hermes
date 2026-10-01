using Hermes.Interfaces;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace Hermes;

/*
    // Steps are awaited one by one (children may be AsyncDelegateCommand or any ICommand)
    var pipeline = new AsyncSequentialCommand(stopOnFailure: true)
        .OnError((cmd, ex) => Logger.Error(ex))
        .WithLoadingIndicator(b => IsBusy = b);
    pipeline.RegisterCommand(validateAsyncCommand);
    pipeline.RegisterCommand(saveAsyncCommand);

    // Steps run concurrently, max 4 at a time
    var refreshAll = new AsyncParallelCommand(maxDegreeOfParallelism: 4);
    refreshAll.RegisterCommand(loadUsers);
    refreshAll.RegisterCommand(loadOrders);
 */

internal static class AsyncStep
{
    /// <summary>Runs a child: awaits IAsyncCommand, executes ordinary ICommand synchronously.</summary>
    internal static Task RunAsync(ICommand command, object? parameter, CancellationToken token)
    {
        if (command is IAsyncCommand asyncCommand)
            return asyncCommand.ExecuteAsync(parameter, token);

        command.Execute(parameter);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Awaits registered commands strictly one after another. Supports cancellation, loading indicator,
/// concurrency modes, timeout and retry of the whole sequence.
/// </summary>
public class AsyncSequentialCommand : AsyncCommandBase
{
    private readonly CommandCollection _commands;
    private readonly bool _stopOnFailure;
    private Action<ICommand, Exception>? _errorHandler;

    public AsyncSequentialCommand(bool stopOnFailure = true)
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

    public AsyncSequentialCommand OnError(Action<ICommand, Exception> errorHandler)
    {
        _errorHandler = errorHandler;
        return this;
    }

    public async Task ExecuteAsync(object? parameter = null, CancellationToken cancellationToken = default)
    {
        if (!CanExecute(parameter)) return;
        await RunAsync(null, ct => RunStepsAsync(parameter, ct), null, cancellationToken);
    }

    private async Task RunStepsAsync(object? parameter, CancellationToken token)
    {
        List<Exception>? errors = null;

        foreach (var command in _commands.Snapshot())
        {
            token.ThrowIfCancellationRequested();

            if (!command.CanExecute(parameter))
            {
                if (_stopOnFailure) break;
                continue;
            }

            try
            {
                await AsyncStep.RunAsync(command, parameter, token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                if (_errorHandler != null)
                {
                    _errorHandler(command, ex);
                }
                else if (_stopOnFailure)
                {
                    throw;
                }
                else
                {
                    (errors ??= new List<Exception>()).Add(ex);
                }

                if (_stopOnFailure) break;
            }
        }

        if (errors is { Count: 1 })
            throw errors[0];
        if (errors is { Count: > 1 })
            throw new AggregateException(errors);
    }

    protected override bool CanExecute(object? parameter)
    {
        var snapshot = _commands.Snapshot();
        if (snapshot.Count == 0) return false;

        bool childrenOk = _stopOnFailure
            ? snapshot.All(c => c.CanExecute(parameter))
            : snapshot.Any(c => c.CanExecute(parameter));
        return CanRunNow(childrenOk);
    }

    protected override async void Execute(object? parameter) => await ExecuteAsync(parameter);

    protected override Task ExecuteUntypedAsync(object? parameter, CancellationToken cancellationToken)
        => ExecuteAsync(parameter, cancellationToken);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _commands.Clear();
        base.Dispose(disposing);
    }
}

/// <summary>
/// Starts all executable registered commands concurrently and completes when all are done.
/// Errors are aggregated (AggregateException) unless a handler is configured.
/// </summary>
public class AsyncParallelCommand : AsyncCommandBase
{
    private readonly CommandCollection _commands;
    private readonly CanExecuteMode _mode;
    private readonly int _maxDegreeOfParallelism;

    /// <param name="mode">How children's CanExecute is combined.</param>
    /// <param name="maxDegreeOfParallelism">0 or less = unlimited.</param>
    public AsyncParallelCommand(CanExecuteMode mode = CanExecuteMode.Any, int maxDegreeOfParallelism = 0)
    {
        _mode = mode;
        _maxDegreeOfParallelism = maxDegreeOfParallelism;
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

    public async Task ExecuteAsync(object? parameter = null, CancellationToken cancellationToken = default)
    {
        if (!CanExecute(parameter)) return;
        await RunAsync(null, ct => RunAllAsync(parameter, ct), null, cancellationToken);
    }

    private async Task RunAllAsync(object? parameter, CancellationToken token)
    {
        SemaphoreSlim? limiter = _maxDegreeOfParallelism > 0 ? new SemaphoreSlim(_maxDegreeOfParallelism) : null;
        try
        {
            var tasks = new List<Task>();
            foreach (var command in _commands.Snapshot())
            {
                if (!command.CanExecute(parameter)) continue;
                tasks.Add(RunOneAsync(command, parameter, limiter, token));
            }

            var all = Task.WhenAll(tasks);
            try
            {
                await all;
            }
            catch
            {
                // Task.WhenAll rethrows only the first exception; surface all of them.
                if (all.Exception is { InnerExceptions.Count: > 1 } aggregate)
                    throw aggregate;
                throw;
            }
        }
        finally
        {
            limiter?.Dispose();
        }
    }

    private static async Task RunOneAsync(ICommand command, object? parameter, SemaphoreSlim? limiter, CancellationToken token)
    {
        if (limiter != null)
            await limiter.WaitAsync(token);
        try
        {
            await AsyncStep.RunAsync(command, parameter, token);
        }
        finally
        {
            limiter?.Release();
        }
    }

    protected override bool CanExecute(object? parameter)
    {
        var snapshot = _commands.Snapshot();
        if (snapshot.Count == 0) return false;

        bool childrenOk = _mode == CanExecuteMode.All
            ? snapshot.All(c => c.CanExecute(parameter))
            : snapshot.Any(c => c.CanExecute(parameter));
        return CanRunNow(childrenOk);
    }

    protected override async void Execute(object? parameter) => await ExecuteAsync(parameter);

    protected override Task ExecuteUntypedAsync(object? parameter, CancellationToken cancellationToken)
        => ExecuteAsync(parameter, cancellationToken);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _commands.Clear();
        base.Dispose(disposing);
    }
}
