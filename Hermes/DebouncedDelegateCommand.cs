using System.Diagnostics;

namespace Hermes;

/*
    SearchCommand = new DebouncedDelegateCommand(PerformSearch, TimeSpan.FromMilliseconds(500));

    // Even if the user keeps typing, run at least every 2 seconds
    SearchCommand = new DebouncedDelegateCommand(
        PerformSearch, TimeSpan.FromMilliseconds(500), maxWait: TimeSpan.FromSeconds(2));

    // Async: debounce + cancel the previous request
    SearchCommand = new DebouncedAsyncDelegateCommand<string>(
            async (q, ct) => await SearchAsync(q, ct),
            TimeSpan.FromMilliseconds(300))
        .WithConcurrency(ConcurrencyMode.CancelPrevious);
 */

/// <summary>
/// Shared debounce logic. <see cref="WaitAsync"/> returns true only for the LAST call of a burst.
/// With <c>maxWait</c> a burst can't postpone execution longer than maxWait since its first call.
/// </summary>
internal sealed class Debouncer : IDisposable
{
    private readonly TimeSpan _interval;
    private readonly TimeSpan? _maxWait;
    private readonly object _lock = new();
    private CancellationTokenSource? _cts;
    private long? _firstTimestamp;

    public Debouncer(TimeSpan interval, TimeSpan? maxWait)
    {
        _interval = interval;
        _maxWait = maxWait;
    }

    public async Task<bool> WaitAsync()
    {
        CancellationToken token;
        TimeSpan delay;
        lock (_lock)
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            token = _cts.Token;

            _firstTimestamp ??= Stopwatch.GetTimestamp();
            delay = _interval;
            if (_maxWait.HasValue)
            {
                var remaining = _maxWait.Value - Stopwatch.GetElapsedTime(_firstTimestamp.Value);
                if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;
                if (remaining < delay) delay = remaining;
            }
        }

        try
        {
            await Task.Delay(delay, token);
        }
        catch (OperationCanceledException)
        {
            return false;
        }

        lock (_lock)
        {
            if (token.IsCancellationRequested) return false;
            _firstTimestamp = null;
            return true;
        }
    }

    /// <summary>Cancels the pending call (if any) and resets the maxWait window.</summary>
    public void Cancel()
    {
        lock (_lock)
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
            _firstTimestamp = null;
        }
    }

    public void Dispose() => Cancel();
}

/// <summary>
/// DEBOUNCE - executes the command only after a pause without new calls.
/// CanExecute is re-checked right before the delayed execution.
/// </summary>
public class DebouncedDelegateCommand : DelegateCommand
{
    private readonly Debouncer _debouncer;

    public DebouncedDelegateCommand(Action executeMethod, TimeSpan debounceInterval, TimeSpan? maxWait = null)
        : base(executeMethod)
    {
        _debouncer = new Debouncer(debounceInterval, maxWait);
    }

    public DebouncedDelegateCommand(Action executeMethod, Func<bool> canExecuteMethod, TimeSpan debounceInterval, TimeSpan? maxWait = null)
        : base(executeMethod, canExecuteMethod)
    {
        _debouncer = new Debouncer(debounceInterval, maxWait);
    }

    /// <summary>Cancels the pending (not yet executed) call, if any.</summary>
    public void CancelPending() => _debouncer.Cancel();

    protected override async void Execute(object? parameter)
    {
        if (await _debouncer.WaitAsync() && !IsDisposed && CanExecute(parameter))
            base.Execute(parameter);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _debouncer.Dispose();
        base.Dispose(disposing);
    }
}

/// <summary>
/// DEBOUNCE with typed parameter. The last parameter wins.
/// </summary>
public class DebouncedDelegateCommand<T> : DelegateCommand<T>
{
    private readonly Debouncer _debouncer;

    public DebouncedDelegateCommand(Action<T> executeMethod, TimeSpan debounceInterval, TimeSpan? maxWait = null)
        : base(executeMethod)
    {
        _debouncer = new Debouncer(debounceInterval, maxWait);
    }

    public DebouncedDelegateCommand(Action<T> executeMethod, Func<T, bool> canExecuteMethod, TimeSpan debounceInterval, TimeSpan? maxWait = null)
        : base(executeMethod, canExecuteMethod)
    {
        _debouncer = new Debouncer(debounceInterval, maxWait);
    }

    public void CancelPending() => _debouncer.Cancel();

    protected override async void Execute(object? parameter)
    {
        if (await _debouncer.WaitAsync() && !IsDisposed && CanExecute(parameter))
            base.Execute(parameter);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _debouncer.Dispose();
        base.Dispose(disposing);
    }
}

/// <summary>
/// Async DEBOUNCE: waits for a pause, then runs the async action (with all async features:
/// concurrency modes, timeout, retry, cancellation). Combine with
/// <c>WithConcurrency(ConcurrencyMode.CancelPrevious)</c> for search-as-you-type, otherwise
/// CanExecute is false while a previous run is still in progress.
/// </summary>
public class DebouncedAsyncDelegateCommand : AsyncDelegateCommand
{
    private readonly Debouncer _debouncer;

    public DebouncedAsyncDelegateCommand(Func<Task> executeMethod, TimeSpan debounceInterval, TimeSpan? maxWait = null)
        : base(executeMethod)
    {
        _debouncer = new Debouncer(debounceInterval, maxWait);
    }

    public DebouncedAsyncDelegateCommand(Func<CancellationToken, Task> executeMethod, TimeSpan debounceInterval, TimeSpan? maxWait = null)
        : base(executeMethod)
    {
        _debouncer = new Debouncer(debounceInterval, maxWait);
    }

    public void CancelPending() => _debouncer.Cancel();

    protected override async void Execute(object? parameter)
    {
        if (await _debouncer.WaitAsync() && !IsDisposed && CanExecute(parameter))
            await ExecuteAsync();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _debouncer.Dispose();
        base.Dispose(disposing);
    }
}

/// <summary>
/// Async DEBOUNCE with typed parameter. The last parameter wins.
/// </summary>
public class DebouncedAsyncDelegateCommand<T> : AsyncDelegateCommand<T>
{
    private readonly Debouncer _debouncer;

    public DebouncedAsyncDelegateCommand(Func<T, Task> executeMethod, TimeSpan debounceInterval, TimeSpan? maxWait = null)
        : base(executeMethod)
    {
        _debouncer = new Debouncer(debounceInterval, maxWait);
    }

    public DebouncedAsyncDelegateCommand(Func<T, CancellationToken, Task> executeMethod, TimeSpan debounceInterval, TimeSpan? maxWait = null)
        : base(executeMethod)
    {
        _debouncer = new Debouncer(debounceInterval, maxWait);
    }

    public void CancelPending() => _debouncer.Cancel();

    protected override async void Execute(object? parameter)
    {
        if (await _debouncer.WaitAsync() && !IsDisposed && CanExecute(parameter))
            await ExecuteAsync(CommandParameter.Cast<T>(parameter));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _debouncer.Dispose();
        base.Dispose(disposing);
    }
}
