using BenchmarkDotNet.Attributes;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace Hermes.Benchmarks;

public sealed class BenchViewModel : INotifyPropertyChanged
{
    private int _value;
    private BenchViewModel? _nested;

    public event PropertyChangedEventHandler? PropertyChanged;

    public int Value { get => _value; set { _value = value; Raise(); } }
    public BenchViewModel? Nested { get => _nested; set { _nested = value; Raise(); } }

    private void Raise([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// Overhead of the synchronous command pipeline compared to a plain delegate.
/// CommandManager integration is disabled (it needs a WPF dispatcher and would dominate the numbers).
/// </summary>
[MemoryDiagnoser]
public class SyncCommandBenchmarks
{
    private int _counter;
    private Action _plain = null!;
    private ICommand _simple = null!;
    private ICommand _withHooks = null!;
    private ICommand _withLogging = null!;
    private ICommand _typed = null!;
    private ICommand _throttled = null!;

    [GlobalSetup]
    public void Setup()
    {
        _plain = () => _counter++;
        _simple = new DelegateCommand(() => _counter++).UseCommandManager(false);
        _withHooks = new DelegateCommand(() => _counter++)
            .BeforeExecute(() => _counter++)
            .AfterExecute(() => _counter++)
            .UseCommandManager(false);
        _withLogging = new DelegateCommand(() => _counter++).WithLogging(_ => { }).UseCommandManager(false);
        _typed = new DelegateCommand<string>(_ => _counter++).UseCommandManager(false);
        _throttled = new ThrottledDelegateCommand(() => _counter++, TimeSpan.FromMilliseconds(1)).UseCommandManager(false);
    }

    [Benchmark(Baseline = true)]
    public void PlainDelegate() => _plain();

    [Benchmark]
    public void DelegateCommand_Execute() => _simple.Execute(null);

    [Benchmark]
    public bool DelegateCommand_CanExecute() => _simple.CanExecute(null);

    [Benchmark]
    public void DelegateCommand_BeforeAfter() => _withHooks.Execute(null);

    [Benchmark]
    public void DelegateCommand_Logging() => _withLogging.Execute(null);

    [Benchmark]
    public void DelegateCommandT_Execute() => _typed.Execute("x");

    [Benchmark]
    public void Throttled_Execute() => _throttled.Execute(null);
}

[MemoryDiagnoser]
public class AsyncCommandBenchmarks
{
    private AsyncDelegateCommand _async = null!;
    private AsyncDelegateCommand _parallel = null!;
    private AsyncDelegateCommand _withPolicy = null!;

    [GlobalSetup]
    public void Setup()
    {
        _async = new AsyncDelegateCommand(() => Task.CompletedTask).UseCommandManager(false);
        _parallel = new AsyncDelegateCommand(() => Task.CompletedTask)
            .WithConcurrency(ConcurrencyMode.Parallel)
            .UseCommandManager(false);
        _withPolicy = new AsyncDelegateCommand(ct => Task.CompletedTask)
            .WithTimeout(TimeSpan.FromSeconds(5))
            .WithRetry(2)
            .UseCommandManager(false);
    }

    [Benchmark(Baseline = true)]
    public Task AwaitPlainTask() => Task.CompletedTask;

    [Benchmark]
    public Task ExecuteAsync_DisableWhileRunning() => _async.ExecuteAsync();

    [Benchmark]
    public Task ExecuteAsync_Parallel() => _parallel.ExecuteAsync();

    [Benchmark]
    public Task ExecuteAsync_TimeoutAndRetry() => _withPolicy.ExecuteAsync();
}

/// <summary>
/// Cost of ObservesProperty: creating the observer (expression analysis) and notifying on change.
/// </summary>
[MemoryDiagnoser]
public class PropertyObserverBenchmarks
{
    private BenchViewModel _vm = null!;
    private DelegateCommand _flat = null!;
    private DelegateCommand _nested = null!;
    private int _raised;

    [GlobalSetup]
    public void Setup()
    {
        _vm = new BenchViewModel { Nested = new BenchViewModel() };
        var vm = _vm;

        _flat = new DelegateCommand(() => { }).UseCommandManager(false).ObservesProperty(() => vm.Value);
        ((ICommand)_flat).CanExecuteChanged += (_, _) => _raised++;

        _nested = new DelegateCommand(() => { }).UseCommandManager(false).ObservesProperty(() => vm.Nested!.Value);
        ((ICommand)_nested).CanExecuteChanged += (_, _) => _raised++;
    }

    // A fresh view model per call: observers of abandoned commands must not pile up on the shared _vm
    // (they would slow down the Notify_* benchmarks). The cost of the extra view model is part of the result.
    [Benchmark]
    public DelegateCommand Create_ObservesProperty()
    {
        var vm = new BenchViewModel();
        return new DelegateCommand(() => { }).UseCommandManager(false).ObservesProperty(() => vm.Value);
    }

    [Benchmark]
    public DelegateCommand Create_ObservesNestedProperty()
    {
        var vm = new BenchViewModel { Nested = new BenchViewModel() };
        return new DelegateCommand(() => { }).UseCommandManager(false).ObservesProperty(() => vm.Nested!.Value);
    }

    [Benchmark(Baseline = true)]
    public void Notify_NoObserver() => new BenchViewModel().Value++;

    [Benchmark]
    public void Notify_FlatProperty() => _vm.Value++;

    [Benchmark]
    public void Notify_NestedProperty() => _vm.Nested!.Value++;

    [Benchmark]
    public void Notify_NestedReplaced() => _vm.Nested = new BenchViewModel();
}

[MemoryDiagnoser]
public class CompositeBenchmarks
{
    private CompositeCommand _composite = null!;
    private SequentialCompositeCommand _sequential = null!;
    private int _counter;

    [Params(2, 10)]
    public int Children { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _composite = new CompositeCommand().UseCommandManager(false);
        _sequential = new SequentialCompositeCommand().UseCommandManager(false);
        for (int i = 0; i < Children; i++)
        {
            _composite.RegisterCommand(new DelegateCommand(() => _counter++).UseCommandManager(false));
            _sequential.RegisterCommand(new DelegateCommand(() => _counter++).UseCommandManager(false));
        }
    }

    [Benchmark]
    public void Composite_Execute() => ((ICommand)_composite).Execute(null);

    [Benchmark]
    public bool Composite_CanExecute() => ((ICommand)_composite).CanExecute(null);

    [Benchmark]
    public void Sequential_Execute() => ((ICommand)_sequential).Execute(null);
}
