using System.ComponentModel;
using System.Windows.Input;
using Xunit;

namespace Hermes.Tests;

public class AsyncEnhancementsTests
{
    [Fact]
    public async Task DisableWhileRunning_IgnoresSecondCall()
    {
        var gate = new TaskCompletionSource();
        int runs = 0;
        var command = new AsyncDelegateCommand(async () => { runs++; await gate.Task; });

        var first = command.ExecuteAsync();
        await command.ExecuteAsync();   // ignored
        gate.SetResult();
        await first;

        Assert.Equal(1, runs);
    }

    [Fact]
    public async Task CancelPrevious_CancelsRunningExecution()
    {
        var cancelled = new List<string>();
        var completed = new List<string>();
        var command = new AsyncDelegateCommand<string>(async (name, ct) =>
        {
            try { await Task.Delay(2000, ct); completed.Add(name); }
            catch (OperationCanceledException) { cancelled.Add(name); throw; }
        }).WithConcurrency(ConcurrencyMode.CancelPrevious);

        var first = command.ExecuteAsync("a");
        var second = command.ExecuteAsync("b");
        await first;
        command.Cancel();
        await second;

        Assert.Equal(new[] { "a", "b" }, cancelled);
        Assert.Empty(completed);
    }

    [Fact]
    public async Task Queue_ExecutesInCallOrder_OneByOne()
    {
        var log = new List<string>();
        var command = new AsyncDelegateCommand<int>(async n =>
        {
            log.Add($"start{n}");
            await Task.Delay(20);
            log.Add($"end{n}");
        }).WithConcurrency(ConcurrencyMode.Queue);

        var tasks = new[] { command.ExecuteAsync(1), command.ExecuteAsync(2), command.ExecuteAsync(3) };
        await Task.WhenAll(tasks);

        Assert.Equal(new[] { "start1", "end1", "start2", "end2", "start3", "end3" }, log);
        Assert.False(command.IsExecuting);
    }

    [Fact]
    public async Task Parallel_RunsConcurrently()
    {
        var gate = new TaskCompletionSource();
        int started = 0;
        var command = new AsyncDelegateCommand(async () =>
        {
            Interlocked.Increment(ref started);
            await gate.Task;
        }).WithConcurrency(ConcurrencyMode.Parallel);

        var t1 = command.ExecuteAsync();
        var t2 = command.ExecuteAsync();

        Assert.Equal(2, Volatile.Read(ref started));
        Assert.Equal(2, command.RunningCount);
        gate.SetResult();
        await Task.WhenAll(t1, t2);
        Assert.Equal(0, command.RunningCount);
    }

    [Fact]
    public async Task Timeout_ReportsTimeoutException()
    {
        Exception? caught = null;
        var command = new AsyncDelegateCommand(async ct => await Task.Delay(2000, ct))
            .WithTimeout(TimeSpan.FromMilliseconds(50))
            .CatchExceptions(ex => caught = ex);

        await command.ExecuteAsync();

        Assert.IsType<TimeoutException>(caught);
    }

    [Fact]
    public async Task Retry_RepeatsUntilSuccess()
    {
        int attempts = 0;
        var command = new AsyncDelegateCommand(async () =>
        {
            attempts++;
            await Task.Yield();
            if (attempts < 3) throw new InvalidOperationException("fail");
        }).WithRetry(3);

        await command.ExecuteAsync();

        Assert.Equal(3, attempts);
    }

    [Fact]
    public async Task Retry_GivesUpAndReportsLastError()
    {
        int attempts = 0;
        Exception? caught = null;
        var command = new AsyncDelegateCommand(async () =>
        {
            attempts++;
            await Task.Yield();
            throw new InvalidOperationException("always");
        }).WithRetry(2).CatchExceptions(ex => caught = ex);

        await command.ExecuteAsync();

        Assert.Equal(3, attempts);
        Assert.NotNull(caught);
    }

    [Fact]
    public async Task AsyncExceptionHandler_IsAwaited()
    {
        bool handled = false;
        var command = new AsyncDelegateCommand(async () => { await Task.Yield(); throw new Exception("x"); })
            .CatchExceptionsAsync(async _ => { await Task.Delay(10); handled = true; });

        await command.ExecuteAsync();

        Assert.True(handled);
    }

    [Fact]
    public async Task CancelCommand_IsEnabledOnlyWhileExecuting()
    {
        var gate = new TaskCompletionSource();
        var command = new AsyncDelegateCommand(ct => gate.Task);

        Assert.False(command.CancelCommand.CanExecute(null));
        var running = command.ExecuteAsync();
        Assert.True(command.CancelCommand.CanExecute(null));
        gate.SetResult();
        await running;
        Assert.False(command.CancelCommand.CanExecute(null));
    }

    [Fact]
    public async Task IsExecuting_RaisesPropertyChanged()
    {
        var names = new List<string?>();
        var gate = new TaskCompletionSource();
        var command = new AsyncDelegateCommand(() => gate.Task);
        ((INotifyPropertyChanged)command).PropertyChanged += (_, e) => names.Add(e.PropertyName);

        var running = command.ExecuteAsync();
        gate.SetResult();
        await running;

        Assert.Contains(nameof(AsyncDelegateCommand.IsExecuting), names);
    }

    [Fact]
    public void ValueTypeParameter_NullDoesNotThrowInCanExecute()
    {
        var command = new AsyncDelegateCommand<int>(_ => Task.CompletedTask);

        Assert.False(((ICommand)command).CanExecute(null));
        Assert.True(((ICommand)command).CanExecute(5));
    }

    [Fact]
    public async Task ExternalToken_CancelsExecution()
    {
        bool cancelled = false;
        using var cts = new CancellationTokenSource();
        var command = new AsyncDelegateCommand(async ct =>
        {
            try { await Task.Delay(2000, ct); }
            catch (OperationCanceledException) { cancelled = true; throw; }
        });

        var running = command.ExecuteAsync(cts.Token);
        cts.Cancel();
        await running;

        Assert.True(cancelled);
    }
}

[Collection("GlobalHandler")]
public class AsyncCompositeTests
{
    [Fact]
    public async Task Sequential_AwaitsEachAsyncChild()
    {
        var order = new List<int>();
        var slow = new AsyncDelegateCommand(async () => { await Task.Delay(50); order.Add(1); });
        var fast = new AsyncDelegateCommand(async () => { await Task.Yield(); order.Add(2); });

        var seq = new AsyncSequentialCommand();
        seq.RegisterCommand(slow);
        seq.RegisterCommand(fast);

        await seq.ExecuteAsync();

        Assert.Equal(new[] { 1, 2 }, order);
    }

    [Fact]
    public async Task Sequential_StopsOnFailure_AndReportsToHandler()
    {
        var order = new List<int>();
        Exception? error = null;
        var ok = new AsyncDelegateCommand(async () => { await Task.Yield(); order.Add(1); });
        var bad = new AsyncDelegateCommand(async () => { await Task.Yield(); throw new InvalidOperationException("boom"); });
        var never = new AsyncDelegateCommand(async () => { await Task.Yield(); order.Add(3); });

        var seq = new AsyncSequentialCommand(stopOnFailure: true).OnError((_, ex) => error = ex);
        seq.RegisterCommand(ok);
        seq.RegisterCommand(bad);
        seq.RegisterCommand(never);

        await seq.ExecuteAsync();

        Assert.Equal(new[] { 1 }, order);
        Assert.NotNull(error);
    }

    [Fact]
    public async Task Sequential_WithoutHandler_ThrowsChildError()
    {
        var seq = new AsyncSequentialCommand();
        seq.RegisterCommand(new DelegateCommand(() => throw new InvalidOperationException("sync boom")));

        await Assert.ThrowsAsync<InvalidOperationException>(() => seq.ExecuteAsync());
    }

    [Fact]
    public async Task Parallel_StartsAllChildrenBeforeAnyFinishes()
    {
        var gate = new TaskCompletionSource();
        int started = 0;
        Func<AsyncDelegateCommand> make = () => new AsyncDelegateCommand(async () =>
        {
            Interlocked.Increment(ref started);
            await gate.Task;
        });

        var parallel = new AsyncParallelCommand();
        parallel.RegisterCommand(make());
        parallel.RegisterCommand(make());

        var running = parallel.ExecuteAsync();
        Assert.Equal(2, Volatile.Read(ref started));
        gate.SetResult();
        await running;
    }

    [Fact]
    public async Task Parallel_RespectsMaxDegreeOfParallelism()
    {
        int current = 0, peak = 0;
        Func<AsyncDelegateCommand> make = () => new AsyncDelegateCommand(async () =>
        {
            int now = Interlocked.Increment(ref current);
            int seen;
            while (now > (seen = Volatile.Read(ref peak)) && Interlocked.CompareExchange(ref peak, now, seen) != seen) { }
            await Task.Delay(30);
            Interlocked.Decrement(ref current);
        });

        var parallel = new AsyncParallelCommand(maxDegreeOfParallelism: 2);
        for (int i = 0; i < 5; i++) parallel.RegisterCommand(make());

        await parallel.ExecuteAsync();

        Assert.True(peak <= 2, $"peak was {peak}");
    }
}
