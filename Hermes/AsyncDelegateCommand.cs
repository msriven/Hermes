using System.Linq.Expressions;

namespace Hermes;

/*
    LoadDataCommand = new AsyncDelegateCommand(LoadDataAsync)
        .WithLoadingIndicator(isLoading => IsLoading = isLoading)
        .ObservesProperty(() => CanLoad);

    // CancellationToken + timeout + retry
    LoadDataCommand = new AsyncDelegateCommand(async ct =>
    {
        var data = await httpClient.GetAsync(url, ct);
    })
    .WithTimeout(TimeSpan.FromSeconds(10))
    .WithRetry(3, TimeSpan.FromSeconds(1))
    .CatchExceptionsAsync(async ex => await dialogs.ShowErrorAsync(ex.Message));

    // Search: new request cancels the previous one
    SearchCommand = new AsyncDelegateCommand<string>(async (query, ct) => await SearchAsync(query, ct))
        .WithConcurrency(ConcurrencyMode.CancelPrevious);

    // XAML: <Button Command="{Binding LoadDataCommand.CancelCommand}" Content="Cancel" />
    // XAML: <ProgressBar Visibility="{Binding LoadDataCommand.IsExecuting, Converter=...}" />
 */

/// <summary>
/// Async command without parameters. Supports CancellationToken, concurrency modes, timeout, retry,
/// loading indicator and bindable IsExecuting/CancelCommand.
/// </summary>
public class AsyncDelegateCommand : AsyncCommandBase
{
    private readonly Func<CancellationToken, Task> _executeMethod;
    private Func<bool> _canExecuteMethod;
    private Func<Task>? _beforeExecute;
    private Func<Task>? _afterExecute;

    public AsyncDelegateCommand(Func<Task> executeMethod)
        : this(executeMethod is null ? throw new ArgumentNullException(nameof(executeMethod)) : _ => executeMethod(),
               () => true)
    {
    }

    public AsyncDelegateCommand(Func<Task> executeMethod, Func<bool> canExecuteMethod)
        : this(executeMethod is null ? throw new ArgumentNullException(nameof(executeMethod)) : _ => executeMethod(),
               canExecuteMethod)
    {
    }

    public AsyncDelegateCommand(Func<CancellationToken, Task> executeMethod)
        : this(executeMethod, () => true)
    {
    }

    public AsyncDelegateCommand(Func<CancellationToken, Task> executeMethod, Func<bool> canExecuteMethod)
    {
        _executeMethod = executeMethod ?? throw new ArgumentNullException(nameof(executeMethod));
        _canExecuteMethod = canExecuteMethod ?? throw new ArgumentNullException(nameof(canExecuteMethod));
    }

    /// <summary>
    /// Executes the command and returns a task that completes when it finishes.
    /// </summary>
    public async Task ExecuteAsync(CancellationToken externalToken = default)
    {
        if (!CanExecute()) return;
        await RunAsync(_beforeExecute, _executeMethod, _afterExecute, externalToken);
    }

    /// <summary>Not executing (in DisableWhileRunning mode) and the condition is met.</summary>
    public bool CanExecute() => CanRunNow(_canExecuteMethod());

    protected override async void Execute(object? parameter) => await ExecuteAsync();

    protected override bool CanExecute(object? parameter) => CanExecute();

    protected override Task ExecuteUntypedAsync(object? parameter, CancellationToken cancellationToken)
        => ExecuteAsync(cancellationToken);

    #region Fluent API

    public AsyncDelegateCommand ObservesCanExecute(Expression<Func<bool>> canExecuteExpression)
    {
        _canExecuteMethod = canExecuteExpression.Compile();
        ObservesPropertyInternal(canExecuteExpression);
        return this;
    }

    public AsyncDelegateCommand BeforeExecute(Func<Task> beforeAction) { _beforeExecute = beforeAction; return this; }
    public AsyncDelegateCommand AfterExecute(Func<Task> afterAction) { _afterExecute = afterAction; return this; }
    public AsyncDelegateCommand When(Func<bool> canExecuteMethod)
    {
        _canExecuteMethod = canExecuteMethod ?? throw new ArgumentNullException(nameof(canExecuteMethod));
        return this;
    }

    public static AsyncDelegateCommand Create(Func<Task> executeMethod) => new(executeMethod);
    public static AsyncDelegateCommand Create(Func<CancellationToken, Task> executeMethod) => new(executeMethod);

    #endregion
}

/// <summary>
/// Async command with typed parameter.
/// </summary>
public class AsyncDelegateCommand<T> : AsyncCommandBase
{
    private readonly Func<T, CancellationToken, Task> _executeMethod;
    private Func<T, bool> _canExecuteMethod;
    private Func<T, Task>? _beforeExecute;
    private Func<T, Task>? _afterExecute;

    public AsyncDelegateCommand(Func<T, Task> executeMethod)
        : this(executeMethod is null ? throw new ArgumentNullException(nameof(executeMethod)) : (p, _) => executeMethod(p),
               _ => true)
    {
    }

    public AsyncDelegateCommand(Func<T, Task> executeMethod, Func<T, bool> canExecuteMethod)
        : this(executeMethod is null ? throw new ArgumentNullException(nameof(executeMethod)) : (p, _) => executeMethod(p),
               canExecuteMethod)
    {
    }

    public AsyncDelegateCommand(Func<T, CancellationToken, Task> executeMethod)
        : this(executeMethod, _ => true)
    {
    }

    public AsyncDelegateCommand(Func<T, CancellationToken, Task> executeMethod, Func<T, bool> canExecuteMethod)
    {
        _executeMethod = executeMethod ?? throw new ArgumentNullException(nameof(executeMethod));
        _canExecuteMethod = canExecuteMethod ?? throw new ArgumentNullException(nameof(canExecuteMethod));
    }

    public async Task ExecuteAsync(T parameter, CancellationToken externalToken = default)
    {
        if (!CanExecute(parameter)) return;

        Func<Task>? before = _beforeExecute is null ? null : () => _beforeExecute(parameter);
        Func<Task>? after = _afterExecute is null ? null : () => _afterExecute(parameter);

        await RunAsync(before, ct => _executeMethod(parameter, ct), after, externalToken);
    }

    public bool CanExecute(T parameter) => CanRunNow(_canExecuteMethod(parameter));

    protected override async void Execute(object? parameter)
        => await ExecuteAsync(CommandParameter.Cast<T>(parameter));

    protected override bool CanExecute(object? parameter)
        => CommandParameter.TryCast<T>(parameter, out var typed) && CanExecute(typed);

    protected override Task ExecuteUntypedAsync(object? parameter, CancellationToken cancellationToken)
        => ExecuteAsync(CommandParameter.Cast<T>(parameter), cancellationToken);

    #region Fluent API

    public AsyncDelegateCommand<T> ObservesCanExecute(Expression<Func<bool>> canExecuteExpression)
    {
        var compiled = canExecuteExpression.Compile();
        _canExecuteMethod = _ => compiled();
        ObservesPropertyInternal(canExecuteExpression);
        return this;
    }

    public AsyncDelegateCommand<T> BeforeExecute(Func<T, Task> beforeAction) { _beforeExecute = beforeAction; return this; }
    public AsyncDelegateCommand<T> AfterExecute(Func<T, Task> afterAction) { _afterExecute = afterAction; return this; }
    public AsyncDelegateCommand<T> When(Func<T, bool> canExecuteMethod)
    {
        _canExecuteMethod = canExecuteMethod ?? throw new ArgumentNullException(nameof(canExecuteMethod));
        return this;
    }

    public static AsyncDelegateCommand<T> Create(Func<T, Task> executeMethod) => new(executeMethod);
    public static AsyncDelegateCommand<T> Create(Func<T, CancellationToken, Task> executeMethod) => new(executeMethod);

    #endregion
}
