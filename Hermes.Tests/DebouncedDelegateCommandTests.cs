using System.Windows.Input;
using Xunit;

namespace Hermes.Tests;

public class DebouncedDelegateCommandTests
{
    [Fact]
    public async Task Execute_DelaysExecution()
    {
        int count = 0;
        var command = new DebouncedDelegateCommand(
            () => count++,
            TimeSpan.FromMilliseconds(50));

        ((ICommand)command).Execute(null);
        Assert.Equal(0, count); // Not yet executed

        await Task.Delay(100);
        Assert.Equal(1, count); // Now executed
    }

    [Fact]
    public async Task Execute_CancelsPreviousOnNewCall()
    {
        int count = 0;
        var command = new DebouncedDelegateCommand(
            () => count++,
            TimeSpan.FromMilliseconds(100));

        ((ICommand)command).Execute(null);
        await Task.Delay(50);
        ((ICommand)command).Execute(null); // Reset timer
        await Task.Delay(50);
        ((ICommand)command).Execute(null); // Reset timer again

        await Task.Delay(150);
        Assert.Equal(1, count); // Only last one executed
    }

    [Fact]
    public async Task Dispose_CancelsPendingExecution()
    {
        int count = 0;
        var command = new DebouncedDelegateCommand(
            () => count++,
            TimeSpan.FromMilliseconds(50));

        ((ICommand)command).Execute(null);
        command.Dispose();

        await Task.Delay(100);
        Assert.Equal(0, count);
    }
}

public class DebouncedDelegateCommandGenericTests
{
    [Fact]
    public async Task Execute_DelaysExecution_WithParameter()
    {
        string? result = null;
        var command = new DebouncedDelegateCommand<string>(
            s => result = s,
            TimeSpan.FromMilliseconds(50));

        ((ICommand)command).Execute("hello");
        Assert.Null(result);

        await Task.Delay(100);
        Assert.Equal("hello", result);
    }

    [Fact]
    public async Task Execute_UsesLastParameter()
    {
        string? result = null;
        var command = new DebouncedDelegateCommand<string>(
            s => result = s,
            TimeSpan.FromMilliseconds(50));

        ((ICommand)command).Execute("first");
        await Task.Delay(20);
        ((ICommand)command).Execute("second");
        await Task.Delay(20);
        ((ICommand)command).Execute("third");

        await Task.Delay(100);
        Assert.Equal("third", result);
    }
}
