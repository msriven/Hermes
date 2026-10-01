using System.Windows.Input;
using Xunit;

namespace Hermes.Tests;

public class UndoableCommandTests
{
    [Fact]
    public void Execute_ThenUndo_RevertsAction()
    {
        var list = new List<string>();
        var command = new UndoableCommand(
            () => list.Add("item"),
            () => list.RemoveAt(list.Count - 1));

        ((ICommand)command).Execute(null);
        Assert.Single(list);
        Assert.True(command.HasExecuted);

        command.Undo();
        Assert.Empty(list);
        Assert.False(command.HasExecuted);
    }

    [Fact]
    public void CanUndo_IsFalse_BeforeExecution()
    {
        var command = new UndoableCommand(() => { }, () => { });
        Assert.False(command.CanUndo());
    }

    [Fact]
    public void CanUndo_IsTrue_AfterExecution()
    {
        var command = new UndoableCommand(() => { }, () => { });
        ((ICommand)command).Execute(null);
        Assert.True(command.CanUndo());
    }

    [Fact]
    public void CanUndo_RespectCondition()
    {
        bool allowUndo = true;
        var command = UndoableCommand.Create(() => { }, () => { })
            .WhenUndo(() => allowUndo);

        ((ICommand)command).Execute(null);
        Assert.True(command.CanUndo());

        allowUndo = false;
        Assert.False(command.CanUndo());
    }

    [Fact]
    public void CatchExceptions_InExecute()
    {
        Exception? caught = null;
        var command = UndoableCommand.Create(
            () => throw new InvalidOperationException("exec error"),
            () => { })
            .CatchExceptions(ex => caught = ex);

        ((ICommand)command).Execute(null);

        Assert.NotNull(caught);
        Assert.Equal("exec error", caught!.Message);
    }

    [Fact]
    public void CatchExceptions_InUndo()
    {
        Exception? caught = null;
        var command = UndoableCommand.Create(
            () => { },
            () => throw new InvalidOperationException("undo error"))
            .CatchExceptions(ex => caught = ex);

        ((ICommand)command).Execute(null);
        command.Undo();

        Assert.NotNull(caught);
        Assert.Equal("undo error", caught!.Message);
    }

    [Fact]
    public void WithLogging_LogsBothExecuteAndUndo()
    {
        var logs = new List<string>();
        var command = UndoableCommand.Create(() => { }, () => { })
            .WithLogging(msg => logs.Add(msg));

        ((ICommand)command).Execute(null);
        command.Undo();

        Assert.Equal(2, logs.Count);
        Assert.Contains("Executing", logs[0]);
        Assert.Contains("Undoing", logs[1]);
    }
}

public class UndoableCommandGenericTests
{
    [Fact]
    public void Execute_ThenUndo_WithParameter()
    {
        var list = new List<string>();
        var command = new UndoableCommand<string>(
            item => list.Add(item),
            item => list.Remove(item));

        ((ICommand)command).Execute("hello");
        Assert.Single(list);
        Assert.Equal("hello", list[0]);

        command.Undo();
        Assert.Empty(list);
    }

    [Fact]
    public void Undo_UsesStoredParameter()
    {
        string? undoneItem = null;
        var command = new UndoableCommand<string>(
            item => { },
            item => undoneItem = item);

        ((ICommand)command).Execute("stored");
        command.Undo();

        Assert.Equal("stored", undoneItem);
    }

    [Fact]
    public void When_SetsCanExecuteCondition()
    {
        var command = UndoableCommand<string>.Create(
            s => { },
            s => { })
            .When(s => s?.Length > 3);

        Assert.False(((ICommand)command).CanExecute("ab"));
        Assert.True(((ICommand)command).CanExecute("abcd"));
    }
}
