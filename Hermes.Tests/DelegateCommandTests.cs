using System.Windows.Input;
using Xunit;

namespace Hermes.Tests;

public class DelegateCommandTests
{
    [Fact]
    public void Execute_InvokesAction()
    {
        bool executed = false;
        var command = new DelegateCommand(() => executed = true);
        command.Execute();
        Assert.True(executed);
    }

    [Fact]
    public void CanExecute_ReturnsTrue_ByDefault()
    {
        var command = new DelegateCommand(() => { });
        Assert.True(command.CanExecute());
    }

    [Fact]
    public void CanExecute_ReturnsFalse_WhenConditionFails()
    {
        var command = new DelegateCommand(() => { }, () => false);
        Assert.False(command.CanExecute());
    }

    [Fact]
    public void When_SetsCanExecuteCondition()
    {
        bool canExecute = false;
        var command = DelegateCommand.Create(() => { }).When(() => canExecute);

        Assert.False(command.CanExecute());
        canExecute = true;
        Assert.True(command.CanExecute());
    }

    [Fact]
    public void BeforeAndAfterExecute_AreInvoked_InOrder()
    {
        var order = new List<string>();
        var command = DelegateCommand.Create(() => order.Add("execute"))
            .BeforeExecute(() => order.Add("before"))
            .AfterExecute(() => order.Add("after"));

        ((ICommand)command).Execute(null);

        Assert.Equal(new[] { "before", "execute", "after" }, order);
    }

    [Fact]
    public void CatchExceptions_HandlesErrors()
    {
        Exception? caught = null;
        var command = DelegateCommand.Create(() => throw new InvalidOperationException("test"))
            .CatchExceptions(ex => caught = ex);

        ((ICommand)command).Execute(null);

        Assert.NotNull(caught);
        Assert.Equal("test", caught!.Message);
    }

    [Fact]
    public void WithLogging_InvokesLogger()
    {
        string? logMessage = null;
        var command = DelegateCommand.Create(() => { })
            .WithLogging(msg => logMessage = msg);

        ((ICommand)command).Execute(null);

        Assert.NotNull(logMessage);
        Assert.Contains("Executing command", logMessage);
    }

    [Fact]
    public void Constructor_ThrowsOnNullExecuteMethod()
    {
        Assert.Throws<ArgumentNullException>(() => new DelegateCommand(null!));
    }

    [Fact]
    public void Constructor_ThrowsOnNullCanExecuteMethod()
    {
        Assert.Throws<ArgumentNullException>(() => new DelegateCommand(() => { }, null!));
    }

    [Fact]
    public void Dispose_DoesNotThrow()
    {
        var command = new DelegateCommand(() => { });
        command.Dispose();
    }

    [Fact]
    public void Create_ReturnsFunctionalCommand()
    {
        bool executed = false;
        var command = DelegateCommand.Create(() => executed = true);
        command.Execute();
        Assert.True(executed);
    }
}

public class DelegateCommandGenericTests
{
    [Fact]
    public void Execute_InvokesActionWithParameter()
    {
        string? result = null;
        var command = new DelegateCommand<string>(s => result = s);
        command.Execute("hello");
        Assert.Equal("hello", result);
    }

    [Fact]
    public void CanExecute_UsesParameterInCondition()
    {
        var command = new DelegateCommand<string>(
            s => { },
            s => !string.IsNullOrEmpty(s));

        Assert.False(command.CanExecute(""));
        Assert.True(command.CanExecute("test"));
    }

    [Fact]
    public void When_SetsTypedCondition()
    {
        var command = DelegateCommand<string>.Create(s => { })
            .When(s => s?.Length > 3);

        Assert.False(command.CanExecute("ab"));
        Assert.True(command.CanExecute("abcd"));
    }

    [Fact]
    public void BeforeAndAfterExecute_ReceiveParameter()
    {
        var order = new List<string>();
        var command = DelegateCommand<string>.Create(s => order.Add($"execute:{s}"))
            .BeforeExecute(s => order.Add($"before:{s}"))
            .AfterExecute(s => order.Add($"after:{s}"));

        ((ICommand)command).Execute("test");

        Assert.Equal(new[] { "before:test", "execute:test", "after:test" }, order);
    }

    [Fact]
    public void CatchExceptions_HandlesErrors()
    {
        Exception? caught = null;
        var command = DelegateCommand<string>.Create(
            s => throw new InvalidOperationException(s))
            .CatchExceptions(ex => caught = ex);

        ((ICommand)command).Execute("error msg");

        Assert.NotNull(caught);
        Assert.Equal("error msg", caught!.Message);
    }
}
