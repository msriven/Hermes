using System.Diagnostics;

namespace Hermes;

/*
    // At most once per 100 ms, extra calls are dropped
    ScrollCommand = new ThrottledDelegateCommand(OnScroll, TimeSpan.FromMilliseconds(100));

    // Same, but the LAST dropped call is executed at the end of the interval
    ScrollCommand = new ThrottledDelegateCommand<double>(
        offset => ScrollTo(offset),
        TimeSpan.FromMilliseconds(100),
        executeTrailing: true);
 */

/// <summary>
/// Shared, thread-safe throttle logic. Leading call is immediate. With <c>trailing</c> the last call that
/// was dropped during the interval is executed when the interval ends (on the captured SynchronizationContext).
/// </summary>
internal sealed class Throttler : IDisposable
{
    private readonly TimeSpan _interval;
    private readonly bool _trailing;
    private readonly SynchronizationContext? _context = SynchronizationContext.Current;
    private readonly object _lock = new();
    private long _lastTimestamp;
    private bool _hasRun;
    private object? _pending;
    private bool _hasPending;
    private CancellationTokenSource? _cts;

    public Throttler(TimeSpan interval, bool trailing)
    {
        _interval = interval;
        _trailing = trailing;
    }

    public void Invoke(object? parameter, Action<object?> run)
    {
        bool runNow = false;
        lock (_lock)
        {
            if (!_hasRun || Stopwatch.GetElapsedTime(_lastTimestamp) >= _interval)
            {
                _hasRun = true;
                _lastTimestamp = Stopwatch.GetTimestamp();
                _hasPending = false;
                _pending = null;
                runNow = true;
            }
            else if (_trailing)
            {
                _pending = parameter;
                if (!_hasPending)
                {
                    _hasPending = true;
                    ScheduleTrailing(run);
                }
            }
        }

        if (runNow)
            run(parameter);
    }

    private void ScheduleTrailing(Action<object?> run)
    {
        var remaining = _interval - Stopwatch.GetElapsedTime(_lastTimestamp);
        if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;

        var cts = new CancellationTokenSource();
        _cts = cts;

        _ = Task.Delay(remaining, cts.Token).ContinueWith(t =>
        {
            if (t.IsCanceled) return;

            object? parameter;
            lock (_lock)
            {
                if (!_hasPending) return;
                parameter = _pending;
                _pending = null;
                _hasPending = false;
                _lastTimestamp = Stopwatch.GetTimestamp();
            }

            if (_context != null)
                _context.Post(_ => run(parameter), null);
            else
                run(parameter);
        }, TaskScheduler.Default);
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _hasPending = false;
            _pending = null;
            _cts?.Cancel();
            _cts = null;
        }
    }
}

/// <summary>
/// THROTTLE - executes the command no more than once per interval.
/// The first call is immediate; calls inside the interval are dropped
/// (or the last of them is executed at the end of the interval when <c>executeTrailing</c> is true).
/// </summary>
public class ThrottledDelegateCommand : DelegateCommand
{
    private readonly Throttler _throttler;

    public ThrottledDelegateCommand(Action executeMethod, TimeSpan throttleInterval, bool executeTrailing = false)
        : base(executeMethod)
    {
        _throttler = new Throttler(throttleInterval, executeTrailing);
    }

    public ThrottledDelegateCommand(Action executeMethod, Func<bool> canExecuteMethod, TimeSpan throttleInterval, bool executeTrailing = false)
        : base(executeMethod, canExecuteMethod)
    {
        _throttler = new Throttler(throttleInterval, executeTrailing);
    }

    protected override void Execute(object? parameter)
        => _throttler.Invoke(parameter, p => base.Execute(p));

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _throttler.Dispose();
        base.Dispose(disposing);
    }
}

/// <summary>
/// THROTTLE with typed parameter.
/// </summary>
public class ThrottledDelegateCommand<T> : DelegateCommand<T>
{
    private readonly Throttler _throttler;

    public ThrottledDelegateCommand(Action<T> executeMethod, TimeSpan throttleInterval, bool executeTrailing = false)
        : base(executeMethod)
    {
        _throttler = new Throttler(throttleInterval, executeTrailing);
    }

    public ThrottledDelegateCommand(Action<T> executeMethod, Func<T, bool> canExecuteMethod, TimeSpan throttleInterval, bool executeTrailing = false)
        : base(executeMethod, canExecuteMethod)
    {
        _throttler = new Throttler(throttleInterval, executeTrailing);
    }

    protected override void Execute(object? parameter)
        => _throttler.Invoke(parameter, p => base.Execute(p));

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _throttler.Dispose();
        base.Dispose(disposing);
    }
}
