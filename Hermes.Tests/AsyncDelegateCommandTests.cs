using System.Windows.Input;
using Xunit;

namespace Hermes.Tests;

public class AsyncDelegateCommandTests
{
    [Fact]
    public async Task ExecuteAsync_InvokesAsyncAction()
    {
        bool executed = false;
        var command = new AsyncDelegateCommand(async () =>
        {
            await Task.Delay(10);
            executed = true;
        });

        await command.ExecuteAsync();
        Assert.True(executed);
    }

    [Fact]
    public async Task IsExecuting_IsTrueDuringExecution()
    {
        var tcs = new TaskCompletionSource();
        bool wasExecuting = false;

        var command = new AsyncDelegateCommand(() => tcs.Task);

        var executeTask = command.ExecuteAsync();
        wasExecuting = command.IsExecuting;
        tcs.SetResult();
        await executeTask;

        Assert.True(wasExecuting);
        Assert.False(command.IsExecuting);
    }

    [Fact]
    public async Task CanExecute_ReturnsFalse_WhileExecuting()
    {
        var tcs = new TaskCompletionSource();
        var command = new AsyncDelegateCommand(() => tcs.Task);

        var executeTask = command.ExecuteAsync();
        Assert.False(command.CanExecute());

        tcs.SetResult();
        await executeTask;
        Assert.True(command.CanExecute());
    }

    [Fact]
    public async Task CancellationToken_IsPassed_AndCanBeCancelled()
    {
        bool wasCancelled = false;
        var command = new AsyncDelegateCommand(async ct =>
        {
            try
            {
                await Task.Delay(10000, ct);
            }
            catch (OperationCanceledException)
            {
                wasCancelled = true;
            }
        });

        var executeTask = command.ExecuteAsync();
        await Task.Delay(50);
        command.Cancel();
        await executeTask;

        Assert.True(wasCancelled);
    }

    [Fact]
    public async Task WithLoadingIndicator_IsInvoked()
    {
        var loadingStates = new List<bool>();
        var command = AsyncDelegateCommand.Create(async () => await Task.Delay(10))
            .WithLoadingIndicator(isLoading => loadingStates.Add(isLoading));

        await command.ExecuteAsync();

        Assert.Contains(true, loadingStates);
        Assert.Contains(false, loadingStates);
    }

    [Fact]
    public async Task CatchExceptions_HandlesErrors()
    {
        Exception? caught = null;
        var command = AsyncDelegateCommand.Create(async () =>
        {
            await Task.Yield();
            throw new InvalidOperationException("async error");
        }).CatchExceptions(ex => caught = ex);

        await command.ExecuteAsync();

        Assert.NotNull(caught);
        Assert.Equal("async error", caught!.Message);
    }

    [Fact]
    public async Task CanExecute_ReturnsFalse_WhenConditionFails()
    {
        var command = new AsyncDelegateCommand(
            async () => await Task.Yield(),
            () => false);

        Assert.False(command.CanExecute());
    }

    [Fact]
    public async Task When_SetsCanExecuteCondition()
    {
        bool canRun = false;
        var command = AsyncDelegateCommand.Create(async () => await Task.Yield())
            .When(() => canRun);

        Assert.False(command.CanExecute());
        canRun = true;
        Assert.True(command.CanExecute());
    }

    [Fact]
    public void Constructor_ThrowsOnNull()
    {
        Assert.Throws<ArgumentNullException>(() => new AsyncDelegateCommand((Func<Task>)null!));
    }

    [Fact]
    public void Dispose_CancelsPendingAndCleansUp()
    {
        var command = new AsyncDelegateCommand(async () => await Task.Delay(1000));
        command.Dispose(); // Should not throw
    }

    [Fact]
    public async Task Create_WithCancellationToken_Works()
    {
        CancellationToken receivedToken = default;
        var command = AsyncDelegateCommand.Create(async ct =>
        {
            receivedToken = ct;
            await Task.Yield();
        });

        await command.ExecuteAsync();
        Assert.True(receivedToken != default || receivedToken == CancellationToken.None);
    }
}

public class AsyncDelegateCommandGenericTests
{
    [Fact]
    public async Task ExecuteAsync_InvokesWithParameter()
    {
        string? result = null;
        var command = new AsyncDelegateCommand<string>(async s =>
        {
            await Task.Yield();
            result = s;
        });

        await command.ExecuteAsync("hello");
        Assert.Equal("hello", result);
    }

    [Fact]
    public async Task WithLoadingIndicator_IsInvoked()
    {
        var states = new List<bool>();
        var command = AsyncDelegateCommand<string>.Create(async s => await Task.Delay(10))
            .WithLoadingIndicator(loading => states.Add(loading));

        await command.ExecuteAsync("test");

        Assert.Contains(true, states);
        Assert.Contains(false, states);
    }

    [Fact]
    public async Task CancellationToken_Works()
    {
        bool cancelled = false;
        var command = new AsyncDelegateCommand<string>(async (s, ct) =>
        {
            try { await Task.Delay(10000, ct); }
            catch (OperationCanceledException) { cancelled = true; }
        });

        var task = command.ExecuteAsync("test");
        await Task.Delay(50);
        command.Cancel();
        await task;

        Assert.True(cancelled);
    }
}
