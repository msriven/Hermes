using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using Hermes.Behaviors;
using Xunit;

namespace Hermes.Tests;

public class ProgressCommandTests
{
    [Fact]
    public async Task ReportsProgress_UpdatesPropertyAndRaisesEvent()
    {
        var received = new List<int>();
        var done = new TaskCompletionSource();
        var command = new AsyncProgressCommand<int>(async (progress, ct) =>
        {
            progress.Report(10);
            progress.Report(50);
            await Task.Yield();
        });
        command.ProgressChanged += (_, value) =>
        {
            lock (received)
            {
                received.Add(value);
                if (received.Count == 2) done.TrySetResult();
            }
        };

        await command.ExecuteAsync();
        await done.Task.WaitAsync(TimeSpan.FromSeconds(2));

        lock (received)
            Assert.Equal(new[] { 10, 50 }, received);
        Assert.Equal(50, command.Progress);
    }

    [Fact]
    public async Task Typed_PassesParameterAndSupportsCancel()
    {
        var gate = new TaskCompletionSource();
        string? seen = null;
        var command = new AsyncProgressCommand<string, double>(async (name, progress, ct) =>
        {
            seen = name;
            gate.TrySetResult();
            await Task.Delay(5000, ct);
        });

        var running = command.ExecuteAsync("job");
        await gate.Task;
        command.Cancel();
        await running;

        Assert.Equal("job", seen);
        Assert.False(command.IsExecuting);
    }

    [Fact]
    public async Task AsyncHooks_AreInvokedInOrder()
    {
        var order = new List<string>();
        var command = new AsyncDelegateCommand(async () => { await Task.Yield(); order.Add("run"); })
            .BeforeExecuteAsync(async () => { await Task.Yield(); order.Add("before"); })
            .AfterExecuteAsync(async () => { await Task.Yield(); order.Add("after"); });

        await command.ExecuteAsync();

        Assert.Equal(new[] { "before", "run", "after" }, order);
    }
}

public class DebounceMaxWaitTests
{
    [Fact]
    public async Task MaxWait_ForcesExecutionDuringContinuousCalls()
    {
        int count = 0;
        var command = new DebouncedDelegateCommand(
            () => Interlocked.Increment(ref count),
            TimeSpan.FromMilliseconds(100),
            maxWait: TimeSpan.FromMilliseconds(250));

        // Call every 40 ms for ~600 ms: plain debounce would never fire before the burst ends.
        for (int i = 0; i < 15; i++)
        {
            ((ICommand)command).Execute(null);
            await Task.Delay(40);
        }

        Assert.True(Volatile.Read(ref count) >= 1, "maxWait should have forced at least one execution");

        await Task.Delay(300);
    }

    [Fact]
    public async Task AsyncDebounce_RunsOnlyLastCall()
    {
        var args = new List<string>();
        var command = new DebouncedAsyncDelegateCommand<string>(
            async s => { await Task.Yield(); lock (args) args.Add(s); },
            TimeSpan.FromMilliseconds(60));

        ((ICommand)command).Execute("a");
        ((ICommand)command).Execute("b");
        ((ICommand)command).Execute("c");

        await Task.Delay(300);

        lock (args)
            Assert.Equal(new[] { "c" }, args);
    }
}

public class EventBindingTests
{
    private sealed class UpperConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => "converted";
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }

    private sealed class DenyFilter : IEventFilter
    {
        public bool ShouldExecute(object? sender, EventArgs args) => false;
    }

    private static Button Bind(DelegateCommand<object> command, string eventName = "Click")
    {
        var button = new Button();
        EventBinding.SetEventName(button, eventName);
        EventBinding.SetCommand(button, command);
        return button;
    }

    [Fact]
    public void ClrEvent_ExecutesCommand_WithCommandParameter()
    {
        Sta.Run(() =>
        {
            object? received = null;
            var button = Bind(new DelegateCommand<object>(p => received = p));
            EventBinding.SetCommandParameter(button, "hello");

            button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

            Assert.Equal("hello", received);
        });
    }

    [Fact]
    public void PassSender_PassesElement()
    {
        Sta.Run(() =>
        {
            object? received = null;
            var button = Bind(new DelegateCommand<object>(p => received = p));
            EventBinding.SetPassSender(button, true);

            button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

            Assert.Same(button, received);
        });
    }

    [Fact]
    public void PassEventArgs_And_MarkHandled()
    {
        Sta.Run(() =>
        {
            object? received = null;
            var button = Bind(new DelegateCommand<object>(p => received = p));
            EventBinding.SetPassEventArgs(button, true);
            EventBinding.SetMarkHandled(button, true);

            var args = new RoutedEventArgs(ButtonBase.ClickEvent);
            button.RaiseEvent(args);

            Assert.IsType<RoutedEventArgs>(received);
            Assert.True(args.Handled);
        });
    }

    [Fact]
    public void Converter_TransformsParameter()
    {
        Sta.Run(() =>
        {
            object? received = null;
            var button = Bind(new DelegateCommand<object>(p => received = p));
            EventBinding.SetEventArgsConverter(button, new UpperConverter());

            button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

            Assert.Equal("converted", received);
        });
    }

    [Fact]
    public void Filter_CanBlockExecution()
    {
        Sta.Run(() =>
        {
            int count = 0;
            var button = Bind(new DelegateCommand<object>(_ => count++));
            EventBinding.SetFilter(button, new DenyFilter());

            button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

            Assert.Equal(0, count);
        });
    }

    [Fact]
    public void RoutedEventByName_Works()
    {
        Sta.Run(() =>
        {
            _ = ButtonBase.ClickEvent; // make sure the routed event is registered
            int count = 0;
            var border = new Border();
            EventBinding.SetEventName(border, "ButtonBase.Click");
            EventBinding.SetCommand(border, new DelegateCommand<object>(_ => count++));

            border.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

            Assert.Equal(1, count);
        });
    }

    [Fact]
    public void CommandAssignedAfterEventName_Works()
    {
        Sta.Run(() =>
        {
            int count = 0;
            var button = new Button();
            EventBinding.SetEventName(button, "Click");
            EventBinding.SetCommand(button, new DelegateCommand<object>(_ => count++));

            button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

            Assert.Equal(1, count);
        });
    }

    [Fact]
    public void ChangingEventName_Resubscribes()
    {
        Sta.Run(() =>
        {
            int count = 0;
            var button = Bind(new DelegateCommand<object>(_ => count++), "MouseDown");
            EventBinding.SetEventName(button, "Click");

            button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

            Assert.Equal(1, count);
        });
    }
}

public class GestureBindingTests
{
    [Fact]
    public void KeyGesture_AddsInputBinding_AndReplacesOnChange()
    {
        Sta.Run(() =>
        {
            var command = new DelegateCommand(() => { });
            var box = new TextBox();
            GestureBinding.SetGesture(box, "Ctrl+S");
            GestureBinding.SetCommand(box, command);

            Assert.Single(box.InputBindings);
            Assert.IsType<KeyGesture>(((InputBinding)box.InputBindings[0]).Gesture);

            GestureBinding.SetGesture(box, "Ctrl+O");
            Assert.Single(box.InputBindings);
        });
    }

    [Fact]
    public void MouseGesture_IsSupported()
    {
        Sta.Run(() =>
        {
            var border = new Border();
            GestureBinding.SetGesture(border, "LeftDoubleClick");
            GestureBinding.SetCommand(border, new DelegateCommand(() => { }));

            Assert.Single(border.InputBindings);
            Assert.IsType<MouseGesture>(((InputBinding)border.InputBindings[0]).Gesture);
        });
    }

    [Fact]
    public void InvalidGesture_AddsNothing()
    {
        Sta.Run(() =>
        {
            var box = new TextBox();
            GestureBinding.SetGesture(box, "NotAGesture+++");
            GestureBinding.SetCommand(box, new DelegateCommand(() => { }));

            Assert.Empty(box.InputBindings);
        });
    }
}

public partial class GeneratedViewModel : TestViewModel
{
    public int Saved;
    public bool AllowSave = true;
    public string? Selected;
    public bool Loaded;
    public string? LastQuery;

    [Command(CanExecute = nameof(CanSave))]
    private void Save() => Saved++;

    private bool CanSave() => AllowSave;

    [Command(CanExecute = nameof(HasFlag), ObservedProperties = new[] { nameof(Flag) })]
    private void Select(string? name) => Selected = name;

    private bool HasFlag => Flag;

    [Command]
    private async Task LoadAsync(CancellationToken ct)
    {
        await Task.Yield();
        Loaded = true;
    }

    [Command(Concurrency = ConcurrencyMode.CancelPrevious, Name = "FindCommand")]
    private async Task SearchAsync(string query, CancellationToken ct)
    {
        await Task.Yield();
        LastQuery = query;
    }
}

public class SourceGeneratorTests
{
    [Fact]
    public void Sync_CommandWithCanExecuteMethod()
    {
        var vm = new GeneratedViewModel();
        Assert.IsType<DelegateCommand>(vm.SaveCommand);
        Assert.Same(vm.SaveCommand, vm.SaveCommand);

        ((ICommand)vm.SaveCommand).Execute(null);
        Assert.Equal(1, vm.Saved);

        vm.AllowSave = false;
        Assert.False(((ICommand)vm.SaveCommand).CanExecute(null));
    }

    [Fact]
    public void Sync_TypedCommand_WithPropertyCondition_AndObservedProperty()
    {
        var vm = new GeneratedViewModel();
        Assert.IsType<DelegateCommand<string>>(vm.SelectCommand);

        Assert.False(((ICommand)vm.SelectCommand).CanExecute("x"));
        vm.Flag = true;
        Assert.True(((ICommand)vm.SelectCommand).CanExecute("x"));

        ((ICommand)vm.SelectCommand).Execute("item");
        Assert.Equal("item", vm.Selected);
    }

    [Fact]
    public async Task Async_Command_TrimsAsyncSuffix()
    {
        var vm = new GeneratedViewModel();
        Assert.IsType<AsyncDelegateCommand>(vm.LoadCommand);

        await vm.LoadCommand.ExecuteAsync();

        Assert.True(vm.Loaded);
    }

    [Fact]
    public async Task Async_TypedCommand_UsesCustomNameAndConcurrency()
    {
        var vm = new GeneratedViewModel();
        Assert.IsType<AsyncDelegateCommand<string>>(vm.FindCommand);
        Assert.Equal(ConcurrencyMode.CancelPrevious, vm.FindCommand.Concurrency);

        await vm.FindCommand.ExecuteAsync("hermes");

        Assert.Equal("hermes", vm.LastQuery);
    }
}
