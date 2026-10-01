using System.Windows.Input;
using Xunit;

namespace Hermes.Tests;

public class ThrottledDelegateCommandTests
{
    [Fact]
    public void Execute_FirstCallExecutesImmediately()
    {
        int count = 0;
        var command = new ThrottledDelegateCommand(
            () => count++,
            TimeSpan.FromMilliseconds(100));

        ((ICommand)command).Execute(null);
        Assert.Equal(1, count);
    }

    [Fact]
    public void Execute_ThrottlesSubsequentCalls()
    {
        int count = 0;
        var command = new ThrottledDelegateCommand(
            () => count++,
            TimeSpan.FromMilliseconds(500));

        ((ICommand)command).Execute(null);
        ((ICommand)command).Execute(null);
        ((ICommand)command).Execute(null);

        Assert.Equal(1, count);
    }

    [Fact]
    public async Task Execute_AllowsAfterInterval()
    {
        int count = 0;
        var command = new ThrottledDelegateCommand(
            () => count++,
            TimeSpan.FromMilliseconds(50));

        ((ICommand)command).Execute(null);
        await Task.Delay(100);
        ((ICommand)command).Execute(null);

        Assert.Equal(2, count);
    }

    [Fact]
    public void CanExecute_Works_WithThrottle()
    {
        bool canExec = true;
        var command = new ThrottledDelegateCommand(
            () => { },
            () => canExec,
            TimeSpan.FromMilliseconds(100));

        Assert.True(((ICommand)command).CanExecute(null));
        canExec = false;
        Assert.False(((ICommand)command).CanExecute(null));
    }
}

public class ThrottledDelegateCommandGenericTests
{
    [Fact]
    public void Execute_FirstCallExecutesImmediately()
    {
        string? result = null;
        var command = new ThrottledDelegateCommand<string>(
            s => result = s,
            TimeSpan.FromMilliseconds(100));

        ((ICommand)command).Execute("test");
        Assert.Equal("test", result);
    }

    [Fact]
    public void Execute_ThrottlesSubsequentCalls()
    {
        int count = 0;
        var command = new ThrottledDelegateCommand<string>(
            s => count++,
            TimeSpan.FromMilliseconds(500));

        ((ICommand)command).Execute("a");
        ((ICommand)command).Execute("b");
        ((ICommand)command).Execute("c");

        Assert.Equal(1, count);
    }
}
