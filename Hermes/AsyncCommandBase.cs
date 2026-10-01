using Hermes.Interfaces;
using System.Windows.Input;

namespace Hermes;

/// <summary>
/// What happens when an async command is invoked while a previous run is still in progress.
/// </summary>
public enum ConcurrencyMode
{
    /// <summary>CanExecute is false while running (default, classic behavior).</summary>
    DisableWhileRunning,

    /// <summary>The running execution is cancelled and the new one starts immediately.</summary>
    CancelPrevious,

    /// <summary>New invocations wait for the running one and execute one by one in call order.</summary>
    Queue,

    /// <summary>Every invocation runs independently and concurrently.</summary>
    Parallel
}

/// <summary>
/// Common engine of all async commands: concurrency modes, cancellation, timeout, retry,
/// loading indicator, sync/async exception handlers, IsExecuting notifications.
/// </summary>
public abstract class AsyncCommandBase : DelegateCommandBase, IAsyncCommand
{
    private readonly object _sync = new();
    private readonly List<CancellationTokenSource> _running = new();
    private readonly SemaphoreSlim _queueGate = new(1, 1);
    private int _runningCount;
    private DelegateCommand? _cancelCommand;

    protected Action<bool>? _loadingIndicator;
    protected Func<Exception, Task>? _asyncExceptionHandler;
    protected ConcurrencyMode _mode = ConcurrencyMode.DisableWhileRunning;
    protected TimeSpan? _timeout;
    protected int _retryCount;
    protected TimeSpan _retryDelay;
    protected Func<Exception, bool>? _retryFilter;

    /// <summary>True while at least one execution (running or queued) is in progress. Bindable.</summary>
    public bool IsExecuting => Volatile.Read(ref _runningCount) > 0;

    /// <summary>Number of executions in progress (running + queued). Bindable.</summary>
    public int RunningCount => Volatile.Read(ref _runningCount);

    /// <summary>Current concurrency mode.</summary>
    public ConcurrencyMode Concurrency => _mode;

    /// <summary>
    /// Command that cancels all running executions. Enabled only while <see cref="IsExecuting"/>.
    /// Bind it to a "Cancel" button.
    /// </summary>
    public ICommand CancelCommand => _cancelCommand ??= new DelegateCommand(Cancel, () => IsExecuting);

    /// <summary>Cancels all running (and queued) executions.</summary>
    public void Cancel()
    {
        CancellationTokenSource[] snapshot;
        lock (_sync)
            snapshot = _running.ToArray();

        foreach (var cts in snapshot)
        {
            try { cts.Cancel(); }
            catch (ObjectDisposedException) { }
        }
    }

    /// <summary>
    /// Applies the concurrency mode to a CanExecute condition.
    /// </summary>
    protected bool CanRunNow(bool condition)
        => condition && (_mode != ConcurrencyMode.DisableWhileRunning || !IsExecuting);

    /// <summary>Implements <see cref="IAsyncCommand.ExecuteAsync"/> for untyped callers.</summary>
    protected abstract Task ExecuteUntypedAsync(object? parameter, CancellationToken cancellationToken);

    Task IAsyncCommand.ExecuteAsync(object? parameter, CancellationToken cancellationToken)
        => ExecuteUntypedAsync(parameter, cancellationToken);

    /// <summary>
    /// Runs one execution applying mode, delay, timeout, retry, cancellation and error handling.
    /// The synchronous part (up to the first real await) already sets IsExecuting.
    /// </summary>
    protected async Task RunAsync(
        Func<Task>? before,
        Func<CancellationToken, Task> execute,
        Func<Task>? after,
        CancellationToken external)
    {
        bool first;
        if (_mode == ConcurrencyMode.DisableWhileRunning)
        {
            if (Interlocked.CompareExchange(ref _runningCount, 1, 0) != 0)
                return;
            first = true;
        }
        else
        {
            first = Interlocked.Increment(ref _runningCount) == 1;
        }

        var cts = CancellationTokenSource.CreateLinkedTokenSource(external);
        lock (_sync)
        {
            if (_mode == ConcurrencyMode.CancelPrevious)
            {
                foreach (var previous in _running)
                {
                    try { previous.Cancel(); }
                    catch (ObjectDisposedException) { }
                }
            }
            _running.Add(cts);
        }

        NotifyRunning(first, true);

        bool gateTaken = false;
        try
        {
            var token = cts.Token;

            if (_mode == ConcurrencyMode.Queue)
            {
                await _queueGate.WaitAsync(token);
                gateTaken = true;
            }

            Log("Executing async command");

            if (_delay.HasValue)
                await Task.Delay(_delay.Value, token);

            if (before != null)
                await before();

            await ExecuteWithPolicyAsync(execute, token);

            if (after != null)
                await after();
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            // cancelled via Cancel(), CancelPrevious, external token or Dispose - expected
        }
        catch (Exception ex)
        {
            if (!await HandleExceptionAsync(ex))
                throw;
        }
        finally
        {
            if (gateTaken)
                _queueGate.Release();

            lock (_sync)
                _running.Remove(cts);
            cts.Dispose();

            bool last = Interlocked.Decrement(ref _runningCount) == 0;
            NotifyRunning(last, false);
        }
    }

    private async Task<bool> HandleExceptionAsync(Exception ex)
    {
        var asyncHandler = _asyncExceptionHandler;
        if (asyncHandler != null)
        {
            await asyncHandler(ex);
            return true;
        }
        return HandleException(ex);
    }

    private async Task ExecuteWithPolicyAsync(Func<CancellationToken, Task> execute, CancellationToken token)
    {
        int attempt = 0;
        while (true)
        {
            try
            {
                await AttemptAsync(execute, token);
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException
                                       && attempt < _retryCount
                                       && (_retryFilter?.Invoke(ex) ?? true))
            {
                attempt++;
                Log($"Retry {attempt}/{_retryCount} after error: {ex.Message}");
                if (_retryDelay > TimeSpan.Zero)
                    await Task.Delay(_retryDelay, token);
            }
        }
    }

    private async Task AttemptAsync(Func<CancellationToken, Task> execute, CancellationToken token)
    {
        if (!_timeout.HasValue)
        {
            await execute(token);
            return;
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeoutCts.CancelAfter(_timeout.Value);
        try
        {
            await execute(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !token.IsCancellationRequested)
        {
            throw new TimeoutException($"Command timed out after {_timeout.Value}.");
        }
    }

    private void NotifyRunning(bool stateChanged, bool isExecuting)
    {
        if (stateChanged)
        {
            _loadingIndicator?.Invoke(isExecuting);
            OnPropertyChanged(nameof(IsExecuting));
            _cancelCommand?.RaiseCanExecuteChanged();
        }
        OnPropertyChanged(nameof(RunningCount));
        RaiseCanExecuteChanged();
    }

    #region Configuration (used by AsyncCommandExtensions)

    internal void ConfigureLoadingIndicator(Action<bool> indicator) => _loadingIndicator = indicator;
    internal void ConfigureConcurrency(ConcurrencyMode mode) => _mode = mode;
    internal void ConfigureTimeout(TimeSpan timeout) => _timeout = timeout;
    internal void ConfigureAsyncExceptionHandler(Func<Exception, Task> handler) => _asyncExceptionHandler = handler;
    internal void ConfigureRetry(int count, TimeSpan delay, Func<Exception, bool>? filter)
    {
        _retryCount = Math.Max(0, count);
        _retryDelay = delay;
        _retryFilter = filter;
    }

    #endregion

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Cancel();
            _cancelCommand?.Dispose();
        }
        base.Dispose(disposing);
    }
}

/// <summary>
/// Fluent configuration of async commands.
/// </summary>
public static class AsyncCommandExtensions
{
    /// <summary>Callback invoked with true when execution starts and false when all executions finish.</summary>
    public static TCommand WithLoadingIndicator<TCommand>(this TCommand command, Action<bool> loadingIndicator)
        where TCommand : AsyncCommandBase
    {
        ArgumentNullException.ThrowIfNull(command);
        command.ConfigureLoadingIndicator(loadingIndicator);
        return command;
    }

    /// <summary>Selects what happens when the command is invoked while already running.</summary>
    public static TCommand WithConcurrency<TCommand>(this TCommand command, ConcurrencyMode mode)
        where TCommand : AsyncCommandBase
    {
        ArgumentNullException.ThrowIfNull(command);
        command.ConfigureConcurrency(mode);
        return command;
    }

    /// <summary>Fails the execution with TimeoutException if it runs longer than the timeout.</summary>
    public static TCommand WithTimeout<TCommand>(this TCommand command, TimeSpan timeout)
        where TCommand : AsyncCommandBase
    {
        ArgumentNullException.ThrowIfNull(command);
        command.ConfigureTimeout(timeout);
        return command;
    }

    /// <summary>
    /// Retries the main action up to <paramref name="retryCount"/> additional times on failure.
    /// Cancellation is never retried. <paramref name="shouldRetry"/> may filter exceptions.
    /// </summary>
    public static TCommand WithRetry<TCommand>(this TCommand command, int retryCount, TimeSpan delay = default, Func<Exception, bool>? shouldRetry = null)
        where TCommand : AsyncCommandBase
    {
        ArgumentNullException.ThrowIfNull(command);
        command.ConfigureRetry(retryCount, delay, shouldRetry);
        return command;
    }

    /// <summary>Asynchronous exception handler (takes priority over CatchExceptions).</summary>
    public static TCommand CatchExceptionsAsync<TCommand>(this TCommand command, Func<Exception, Task> handler)
        where TCommand : AsyncCommandBase
    {
        ArgumentNullException.ThrowIfNull(command);
        command.ConfigureAsyncExceptionHandler(handler);
        return command;
    }
}
