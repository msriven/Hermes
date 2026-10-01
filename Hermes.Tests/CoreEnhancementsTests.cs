using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using Hermes.Interfaces;
using Xunit;

namespace Hermes.Tests;

[Collection("GlobalHandler")]
public class ErrorHandlingTests
{
    [Fact]
    public void PublicExecute_RunsFullPipeline()
    {
        var order = new List<string>();
        Exception? caught = null;
        var command = DelegateCommand.Create(() => { order.Add("run"); throw new InvalidOperationException("x"); })
            .BeforeExecute(() => order.Add("before"))
            .AfterExecute(() => order.Add("after"))
            .CatchExceptions(ex => caught = ex);

        command.Execute();

        Assert.Equal(new[] { "before", "run" }, order);
        Assert.NotNull(caught);
    }

    [Fact]
    public void GlobalExceptionHandler_IsUsedAsFallback()
    {
        Exception? caught = null;
        var previous = DelegateCommandBase.GlobalExceptionHandler;
        DelegateCommandBase.GlobalExceptionHandler = ex => caught = ex;
        try
        {
            var command = new DelegateCommand(() => throw new InvalidOperationException("global"));
            ((ICommand)command).Execute(null);
        }
        finally
        {
            DelegateCommandBase.GlobalExceptionHandler = previous;
        }

        Assert.Equal("global", caught?.Message);
    }

    [Fact]
    public void Composite_ContinuesAfterFailure_AndReportsToHandler()
    {
        var results = new List<string>();
        var errors = new List<Exception>();
        var composite = new CompositeCommand().CatchExceptions(errors.Add);
        composite.RegisterCommand(new DelegateCommand(() => results.Add("a")));
        composite.RegisterCommand(new DelegateCommand(() => throw new InvalidOperationException("bad")));
        composite.RegisterCommand(new DelegateCommand(() => results.Add("c")));

        ((ICommand)composite).Execute(null);

        Assert.Equal(new[] { "a", "c" }, results);
        Assert.Single(errors);
    }

    [Fact]
    public void Composite_WithoutHandler_ThrowsAfterRunningAll()
    {
        var results = new List<string>();
        var composite = new CompositeCommand();
        composite.RegisterCommand(new DelegateCommand(() => throw new InvalidOperationException("bad")));
        composite.RegisterCommand(new DelegateCommand(() => results.Add("after")));

        Assert.Throws<InvalidOperationException>(() => ((ICommand)composite).Execute(null));
        Assert.Equal(new[] { "after" }, results);
    }

    [Fact]
    public void Sequential_WithoutHandler_NoLongerSwallowsExceptions()
    {
        var seq = new SequentialCompositeCommand(stopOnFailure: true);
        seq.RegisterCommand(new DelegateCommand(() => throw new InvalidOperationException("bad")));

        Assert.Throws<InvalidOperationException>(() => ((ICommand)seq).Execute(null));
    }

    [Fact]
    public void Sequential_ContinueMode_CanExecuteRequiresAny()
    {
        var seq = new SequentialCompositeCommand(stopOnFailure: false);
        seq.RegisterCommand(new DelegateCommand(() => { }, () => false));
        seq.RegisterCommand(new DelegateCommand(() => { }, () => true));

        Assert.True(((ICommand)seq).CanExecute(null));
    }

    [Fact]
    public void Composite_RespectIsActive_IgnoresInactiveChildren()
    {
        var results = new List<string>();
        var active = new DelegateCommand(() => results.Add("active")) { IsActive = true };
        var inactive = new DelegateCommand(() => results.Add("inactive")) { IsActive = false };
        var composite = new CompositeCommand { RespectIsActive = true };
        composite.RegisterCommand(active);
        composite.RegisterCommand(inactive);

        ((ICommand)composite).Execute(null);

        Assert.Equal(new[] { "active" }, results);
    }
}

public class ObservationTests
{
    private static (T Command, List<int> Counter) Track<T>(T command) where T : DelegateCommandBase
    {
        var counter = new List<int>();
        ((ICommand)command).CanExecuteChanged += (_, _) => counter.Add(1);
        return (command, counter);
    }

    [Fact]
    public void ObservesProperty_RaisesCanExecuteChanged_ForCapturedLocal()
    {
        var vm = new TestViewModel();
        var (_, counter) = Track(new DelegateCommand(() => { })
            .UseCommandManager(false)
            .ObservesProperty(() => vm.Flag));

        vm.Flag = true;

        Assert.Single(counter);
    }

    [Fact]
    public void ObservesProperty_FollowsReplacedNestedObject()
    {
        var vm = new TestViewModel { Nested = new TestViewModel() };
        var oldNested = vm.Nested;
        var (_, counter) = Track(new DelegateCommand(() => { })
            .UseCommandManager(false)
            .ObservesProperty(() => vm.Nested!.Value));

        oldNested.Value = 1;
        Assert.Single(counter);

        var newNested = new TestViewModel();
        vm.Nested = newNested;
        counter.Clear();

        oldNested.Value = 2;
        Assert.Empty(counter);

        newNested.Value = 3;
        Assert.Single(counter);
    }

    [Fact]
    public void Dispose_StopsObserving()
    {
        var vm = new TestViewModel();
        var (command, counter) = Track(new DelegateCommand(() => { })
            .UseCommandManager(false)
            .ObservesProperty(() => vm.Flag));

        command.Dispose();
        vm.Flag = true;

        Assert.Empty(counter);
    }

    [Fact]
    public void ObservesProperty_Twice_Throws()
    {
        var vm = new TestViewModel();
        var command = new DelegateCommand(() => { }).ObservesProperty(() => vm.Flag);

        Assert.Throws<ArgumentException>(() => command.ObservesProperty(() => vm.Flag));
    }

    [Fact]
    public void ObservesCollection_RaisesOnChange()
    {
        var items = new ObservableCollection<int>();
        var (_, counter) = Track(new DelegateCommand(() => { })
            .UseCommandManager(false)
            .ObservesCollection(items));

        items.Add(1);
        items.Clear();

        Assert.Equal(2, counter.Count);
    }

    [Fact]
    public void UseCommandManager_AfterSubscription_Throws()
    {
        var command = new DelegateCommand(() => { }).UseCommandManager(false);
        ((ICommand)command).CanExecuteChanged += (_, _) => { };

        Assert.Throws<InvalidOperationException>(() => command.UseCommandManager(true));
    }
}

public class ThrottleDebounceEnhancementTests
{
    [Fact]
    public async Task Throttle_Trailing_ExecutesLastDroppedCall()
    {
        var results = new List<string>();

        // Created on a thread-pool thread: no SynchronizationContext, trailing call runs directly.
        await Task.Run(() =>
        {
            var command = new ThrottledDelegateCommand<string>(
                s => { lock (results) results.Add(s); },
                TimeSpan.FromMilliseconds(100),
                executeTrailing: true);

            ((ICommand)command).Execute("a");
            ((ICommand)command).Execute("b");
            ((ICommand)command).Execute("c");
        });

        await Task.Delay(400);

        lock (results)
            Assert.Equal(new[] { "a", "c" }, results);
    }

    [Fact]
    public async Task Throttle_WithoutTrailing_DropsExtraCalls()
    {
        int count = 0;
        var command = new ThrottledDelegateCommand(() => Interlocked.Increment(ref count), TimeSpan.FromMilliseconds(100));

        ((ICommand)command).Execute(null);
        ((ICommand)command).Execute(null);
        await Task.Delay(250);

        Assert.Equal(1, Volatile.Read(ref count));
    }

    [Fact]
    public async Task Debounce_RechecksCanExecuteBeforeRunning()
    {
        int count = 0;
        bool allowed = true;
        var command = new DebouncedDelegateCommand(() => count++, () => allowed, TimeSpan.FromMilliseconds(50));

        ((ICommand)command).Execute(null);
        allowed = false;
        await Task.Delay(150);

        Assert.Equal(0, count);
    }

    [Fact]
    public async Task Debounce_CancelPending_PreventsExecution()
    {
        int count = 0;
        var command = new DebouncedDelegateCommand(() => count++, TimeSpan.FromMilliseconds(50));

        ((ICommand)command).Execute(null);
        command.CancelPending();
        await Task.Delay(150);

        Assert.Equal(0, count);
    }
}

public class UndoRedoTests
{
    [Fact]
    public void ExecuteUndoRedo_Works()
    {
        var list = new List<int>();
        var history = new UndoRedoManager();

        history.Execute(new DelegateOperation(() => list.Add(1), () => list.RemoveAt(list.Count - 1), "add"));
        Assert.True(history.CanUndo);
        Assert.False(history.CanRedo);
        Assert.Equal("add", history.UndoDescription);

        history.Undo();
        Assert.Empty(list);
        Assert.True(history.CanRedo);

        history.Redo();
        Assert.Single(list);
    }

    [Fact]
    public void NewOperation_ClearsRedoStack()
    {
        var history = new UndoRedoManager();
        history.Execute(new DelegateOperation(() => { }, () => { }));
        history.Undo();
        Assert.True(history.CanRedo);

        history.Execute(new DelegateOperation(() => { }, () => { }));

        Assert.False(history.CanRedo);
    }

    [Fact]
    public void Group_IsUndoneAsSingleStep_InReverseOrder()
    {
        var log = new List<string>();
        var history = new UndoRedoManager();

        using (history.BeginGroup("both"))
        {
            history.Execute(new DelegateOperation(() => log.Add("do1"), () => log.Add("undo1")));
            history.Execute(new DelegateOperation(() => log.Add("do2"), () => log.Add("undo2")));
        }

        Assert.Equal(1, history.UndoCount);
        history.Undo();

        Assert.Equal(new[] { "do1", "do2", "undo2", "undo1" }, log);
    }

    [Fact]
    public void Capacity_DropsOldestSteps()
    {
        var history = new UndoRedoManager(capacity: 2);
        for (int i = 0; i < 5; i++)
            history.Execute(new DelegateOperation(() => { }, () => { }));

        Assert.Equal(2, history.UndoCount);
    }

    [Fact]
    public void UndoCommand_ReflectsHistory()
    {
        var history = new UndoRedoManager();
        Assert.False(history.UndoCommand.CanExecute(null));

        history.Execute(new DelegateOperation(() => { }, () => { }));
        Assert.True(history.UndoCommand.CanExecute(null));

        history.UndoCommand.Execute(null);
        Assert.True(history.RedoCommand.CanExecute(null));
    }

    [Fact]
    public void UndoableCommand_WithManager_RecordsMultipleLevels()
    {
        var list = new List<string>();
        var history = new UndoRedoManager();
        var add = new UndoableCommand<string>(s => list.Add(s), s => list.Remove(s)).WithUndoManager(history);

        ((ICommand)add).Execute("a");
        ((ICommand)add).Execute("b");
        Assert.Equal(2, history.UndoCount);

        history.Undo();
        history.Undo();

        Assert.Empty(list);
    }
}
