using System.Linq.Expressions;

namespace Hermes;

/*
    ImportCommand = new AsyncProgressCommand<double>(async (progress, ct) =>
        {
            for (int i = 0; i <= 100; i += 10)
            {
                ct.ThrowIfCancellationRequested();
                await Task.Delay(100, ct);
                progress.Report(i);
            }
        })
        .WithLoadingIndicator(b => IsBusy = b);

    // XAML:
    // <ProgressBar Value="{Binding ImportCommand.Progress, Mode=OneWay}" Maximum="100" />
    // <Button Command="{Binding ImportCommand.CancelCommand}" Content="Cancel" />
    // Code:   ImportCommand.ProgressChanged += (s, value) => ...;
 */

/// <summary>
/// Async command whose action reports progress through <see cref="IProgress{T}"/>.
/// <see cref="Progress"/> (bindable) holds the last reported value, <see cref="ProgressChanged"/> is raised for each report.
/// Reports are marshalled to the SynchronizationContext captured at execution start (UI thread).
/// With ConcurrencyMode.Parallel/Queue Progress is the last value reported by any run.
/// </summary>
public class AsyncProgressCommand<TProgress> : AsyncCommandBase
{
    private readonly Func<IProgress<TProgress>, CancellationToken, Task> _executeMethod;
    private Func<bool> _canExecuteMethod;
    private TProgress? _progress;

    public AsyncProgressCommand(Func<IProgress<TProgress>, CancellationToken, Task> executeMethod)
        : this(executeMethod, () => true)
    {
    }

    public AsyncProgressCommand(Func<IProgress<TProgress>, CancellationToken, Task> executeMethod, Func<bool> canExecuteMethod)
    {
        _executeMethod = executeMethod ?? throw new ArgumentNullException(nameof(executeMethod));
        _canExecuteMethod = canExecuteMethod ?? throw new ArgumentNullException(nameof(canExecuteMethod));
    }

    /// <summary>Last reported value (default at the start of each run). Bindable.</summary>
    public TProgress? Progress
    {
        get => _progress;
        private set
        {
            _progress = value;
            OnPropertyChanged();
        }
    }

    /// <summary>Raised for every reported value.</summary>
    public event EventHandler<TProgress>? ProgressChanged;

    public async Task ExecuteAsync(CancellationToken externalToken = default)
    {
        if (!CanExecute()) return;

        Progress = default;
        var reporter = new OrderedProgress<TProgress>(value =>
        {
            Progress = value;
            ProgressChanged?.Invoke(this, value);
        });

        await RunAsync(null, ct => _executeMethod(reporter, ct), null, externalToken);
    }

    public bool CanExecute() => CanRunNow(_canExecuteMethod());

    protected override async void Execute(object? parameter) => await ExecuteAsync();

    protected override bool CanExecute(object? parameter) => CanExecute();

    protected override Task ExecuteUntypedAsync(object? parameter, CancellationToken cancellationToken)
        => ExecuteAsync(cancellationToken);

    public AsyncProgressCommand<TProgress> When(Func<bool> canExecuteMethod)
    {
        _canExecuteMethod = canExecuteMethod ?? throw new ArgumentNullException(nameof(canExecuteMethod));
        return this;
    }

    public AsyncProgressCommand<TProgress> ObservesCanExecute(Expression<Func<bool>> canExecuteExpression)
    {
        _canExecuteMethod = canExecuteExpression.Compile();
        ObservesPropertyInternal(canExecuteExpression);
        return this;
    }

    public static AsyncProgressCommand<TProgress> Create(Func<IProgress<TProgress>, CancellationToken, Task> executeMethod)
        => new(executeMethod);
}

/// <summary>
/// Async command with a typed parameter that reports progress.
/// </summary>
public class AsyncProgressCommand<TParam, TProgress> : AsyncCommandBase
{
    private readonly Func<TParam, IProgress<TProgress>, CancellationToken, Task> _executeMethod;
    private Func<TParam, bool> _canExecuteMethod;
    private TProgress? _progress;

    public AsyncProgressCommand(Func<TParam, IProgress<TProgress>, CancellationToken, Task> executeMethod)
        : this(executeMethod, _ => true)
    {
    }

    public AsyncProgressCommand(Func<TParam, IProgress<TProgress>, CancellationToken, Task> executeMethod, Func<TParam, bool> canExecuteMethod)
    {
        _executeMethod = executeMethod ?? throw new ArgumentNullException(nameof(executeMethod));
        _canExecuteMethod = canExecuteMethod ?? throw new ArgumentNullException(nameof(canExecuteMethod));
    }

    public TProgress? Progress
    {
        get => _progress;
        private set
        {
            _progress = value;
            OnPropertyChanged();
        }
    }

    public event EventHandler<TProgress>? ProgressChanged;

    public async Task ExecuteAsync(TParam parameter, CancellationToken externalToken = default)
    {
        if (!CanExecute(parameter)) return;

        Progress = default;
        var reporter = new OrderedProgress<TProgress>(value =>
        {
            Progress = value;
            ProgressChanged?.Invoke(this, value);
        });

        await RunAsync(null, ct => _executeMethod(parameter, reporter, ct), null, externalToken);
    }

    public bool CanExecute(TParam parameter) => CanRunNow(_canExecuteMethod(parameter));

    protected override async void Execute(object? parameter)
        => await ExecuteAsync(CommandParameter.Cast<TParam>(parameter));

    protected override bool CanExecute(object? parameter)
        => CommandParameter.TryCast<TParam>(parameter, out var typed) && CanExecute(typed);

    protected override Task ExecuteUntypedAsync(object? parameter, CancellationToken cancellationToken)
        => ExecuteAsync(CommandParameter.Cast<TParam>(parameter), cancellationToken);

    public AsyncProgressCommand<TParam, TProgress> When(Func<TParam, bool> canExecuteMethod)
    {
        _canExecuteMethod = canExecuteMethod ?? throw new ArgumentNullException(nameof(canExecuteMethod));
        return this;
    }

    public static AsyncProgressCommand<TParam, TProgress> Create(Func<TParam, IProgress<TProgress>, CancellationToken, Task> executeMethod)
        => new(executeMethod);
}
