using System.Windows.Input;
using Xunit;

namespace Hermes.Tests;

public class CompositeCommandTests
{
    [Fact]
    public void Execute_InvokesAllRegisteredCommands()
    {
        var results = new List<string>();
        var cmd1 = new DelegateCommand(() => results.Add("first"));
        var cmd2 = new DelegateCommand(() => results.Add("second"));

        var composite = new CompositeCommand();
        composite.RegisterCommand(cmd1);
        composite.RegisterCommand(cmd2);

        ((ICommand)composite).Execute(null);

        Assert.Equal(new[] { "first", "second" }, results);
    }

    [Fact]
    public void CanExecute_AnyMode_ReturnsTrueIfAnyCanExecute()
    {
        var cmd1 = new DelegateCommand(() => { }, () => false);
        var cmd2 = new DelegateCommand(() => { }, () => true);

        var composite = new CompositeCommand(monitorCommandActivity: false);
        composite.RegisterCommand(cmd1);
        composite.RegisterCommand(cmd2);

        Assert.True(((ICommand)composite).CanExecute(null));
    }

    [Fact]
    public void CanExecute_MonitorMode_RequiresAllCanExecute()
    {
        var cmd1 = new DelegateCommand(() => { }, () => false);
        var cmd2 = new DelegateCommand(() => { }, () => true);

        var composite = new CompositeCommand(monitorCommandActivity: true);
        composite.RegisterCommand(cmd1);
        composite.RegisterCommand(cmd2);

        Assert.False(((ICommand)composite).CanExecute(null));
    }

    [Fact]
    public void UnregisterCommand_RemovesCommand()
    {
        var results = new List<string>();
        var cmd = new DelegateCommand(() => results.Add("executed"));

        var composite = new CompositeCommand();
        composite.RegisterCommand(cmd);
        composite.UnregisterCommand(cmd);

        Assert.False(((ICommand)composite).CanExecute(null));
    }

    [Fact]
    public void CanExecute_Empty_ReturnsFalse()
    {
        var composite = new CompositeCommand();
        Assert.False(((ICommand)composite).CanExecute(null));
    }

    [Fact]
    public void RegisteredCommands_ReturnsSnapshot()
    {
        var cmd = new DelegateCommand(() => { });
        var composite = new CompositeCommand();
        composite.RegisterCommand(cmd);

        var registered = composite.RegisteredCommands;
        Assert.Single(registered);
    }

    [Fact]
    public void Dispose_CleansUpAllCommands()
    {
        var cmd1 = new DelegateCommand(() => { });
        var cmd2 = new DelegateCommand(() => { });

        var composite = new CompositeCommand();
        composite.RegisterCommand(cmd1);
        composite.RegisterCommand(cmd2);
        composite.Dispose();

        Assert.Empty(composite.RegisteredCommands);
    }

    [Fact]
    public void Execute_SkipsCommandsThatCannotExecute()
    {
        var results = new List<string>();
        var cmd1 = new DelegateCommand(() => results.Add("first"), () => true);
        var cmd2 = new DelegateCommand(() => results.Add("second"), () => false);
        var cmd3 = new DelegateCommand(() => results.Add("third"), () => true);

        var composite = new CompositeCommand();
        composite.RegisterCommand(cmd1);
        composite.RegisterCommand(cmd2);
        composite.RegisterCommand(cmd3);

        ((ICommand)composite).Execute(null);

        Assert.Equal(new[] { "first", "third" }, results);
    }
}

public class SequentialCompositeCommandTests
{
    [Fact]
    public void Execute_RunsInOrder()
    {
        var order = new List<int>();
        var cmd1 = new DelegateCommand(() => order.Add(1));
        var cmd2 = new DelegateCommand(() => order.Add(2));
        var cmd3 = new DelegateCommand(() => order.Add(3));

        var seq = new SequentialCompositeCommand();
        seq.RegisterCommand(cmd1);
        seq.RegisterCommand(cmd2);
        seq.RegisterCommand(cmd3);

        ((ICommand)seq).Execute(null);

        Assert.Equal(new[] { 1, 2, 3 }, order);
    }

    [Fact]
    public void Execute_StopsOnFailure_WhenConfigured()
    {
        var order = new List<int>();
        var cmd1 = new DelegateCommand(() => order.Add(1));
        var cmd2 = new DelegateCommand(() => throw new Exception("fail"));
        var cmd3 = new DelegateCommand(() => order.Add(3));

        var seq = new SequentialCompositeCommand(stopOnFailure: true)
            .OnError((cmd, ex) => { });
        seq.RegisterCommand(cmd1);
        seq.RegisterCommand(cmd2);
        seq.RegisterCommand(cmd3);

        ((ICommand)seq).Execute(null);

        Assert.Equal(new[] { 1 }, order); // cmd3 not reached
    }

    [Fact]
    public void Execute_ContinuesOnFailure_WhenConfigured()
    {
        var order = new List<int>();
        var cmd1 = new DelegateCommand(() => order.Add(1));
        var cmd2 = new DelegateCommand(() => throw new Exception("fail"));
        var cmd3 = new DelegateCommand(() => order.Add(3));

        var seq = new SequentialCompositeCommand(stopOnFailure: false)
            .OnError((cmd, ex) => { });
        seq.RegisterCommand(cmd1);
        seq.RegisterCommand(cmd2);
        seq.RegisterCommand(cmd3);

        ((ICommand)seq).Execute(null);

        Assert.Equal(new[] { 1, 3 }, order); // cmd3 still runs
    }

    [Fact]
    public void CanExecute_RequiresAll()
    {
        var cmd1 = new DelegateCommand(() => { }, () => true);
        var cmd2 = new DelegateCommand(() => { }, () => false);

        var seq = new SequentialCompositeCommand();
        seq.RegisterCommand(cmd1);
        seq.RegisterCommand(cmd2);

        Assert.False(((ICommand)seq).CanExecute(null));
    }
}
