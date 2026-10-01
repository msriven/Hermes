using System.Linq.Expressions;

namespace Hermes;

/*
    SaveCommand = new DelegateCommand(Save, CanExecuteSave)
        .ObservesProperty(() => CanSave);

    SaveCommand = DelegateCommand.Create(Save)
        .When(CanExecuteSave)
        .BeforeExecute(() => Debug.WriteLine("Before save"))
        .AfterExecute(() => Debug.WriteLine("After save"))
        .CatchExceptions(ex => MessageBox.Show($"Error: {ex.Message}"))
        .WithLogging(msg => Debug.WriteLine(msg))
        .ObservesProperty(() => CanSave);

    SearchCommand = DelegateCommand<string>.Create(Search)
        .When(query => !string.IsNullOrWhiteSpace(query));
 */

/// <summary>
/// Synchronous command without parameters.
/// </summary>
public class DelegateCommand : DelegateCommandBase
{
    private readonly Action _executeMethod;
    private Func<bool> _canExecuteMethod;
    private Action? _afterExecute;
    private Action? _beforeExecute;

    public DelegateCommand(Action executeMethod)
        : this(executeMethod, () => true)
    {
    }

    public DelegateCommand(Action executeMethod, Func<bool> canExecuteMethod)
    {
        _executeMethod = executeMethod ?? throw new ArgumentNullException(nameof(executeMethod));
        _canExecuteMethod = canExecuteMethod ?? throw new ArgumentNullException(nameof(canExecuteMethod));
    }

    /// <summary>
    /// Executes the command synchronously through the full pipeline
    /// (logging, BeforeExecute, action, AfterExecute, exception handler). The configured delay is ignored.
    /// </summary>
    public void Execute() => ExecuteCore();

    public bool CanExecute() => _canExecuteMethod();

    /// <summary>
    /// ICommand entry point. When a delay is configured the execution is deferred.
    /// </summary>
    protected override void Execute(object? parameter)
    {
        if (_delay.HasValue)
        {
            ExecuteDelayed();
            return;
        }
        ExecuteCore();
    }

    private void ExecuteCore()
    {
        try
        {
            Log("Executing command");
            _beforeExecute?.Invoke();
            _executeMethod();
            _afterExecute?.Invoke();
        }
        catch (Exception ex)
        {
            if (!HandleException(ex))
                throw;
        }
    }

    private async void ExecuteDelayed()
    {
        await Task.Delay(_delay!.Value);
        if (IsDisposed) return;
        ExecuteCore();
    }

    protected override bool CanExecute(object? parameter) => CanExecute();

    #region Fluent API

    public DelegateCommand ObservesCanExecute(Expression<Func<bool>> canExecuteExpression)
    {
        _canExecuteMethod = canExecuteExpression.Compile();
        ObservesPropertyInternal(canExecuteExpression);
        return this;
    }

    public DelegateCommand BeforeExecute(Action beforeAction) { _beforeExecute = beforeAction; return this; }
    public DelegateCommand AfterExecute(Action afterAction) { _afterExecute = afterAction; return this; }
    public DelegateCommand When(Func<bool> canExecuteMethod)
    {
        _canExecuteMethod = canExecuteMethod ?? throw new ArgumentNullException(nameof(canExecuteMethod));
        return this;
    }

    public static DelegateCommand Create(Action executeMethod) => new(executeMethod);

    #endregion
}

/// <summary>
/// Synchronous command with typed parameter.
/// </summary>
public class DelegateCommand<T> : DelegateCommandBase
{
    private readonly Action<T> _executeMethod;
    private Func<T, bool> _canExecuteMethod;
    private Action<T>? _beforeExecute;
    private Action<T>? _afterExecute;

    public DelegateCommand(Action<T> executeMethod)
        : this(executeMethod, _ => true)
    {
    }

    public DelegateCommand(Action<T> executeMethod, Func<T, bool> canExecuteMethod)
    {
        _executeMethod = executeMethod ?? throw new ArgumentNullException(nameof(executeMethod));
        _canExecuteMethod = canExecuteMethod ?? throw new ArgumentNullException(nameof(canExecuteMethod));

        ThrowIfInvalidGenericType();
    }

    private static void ThrowIfInvalidGenericType()
    {
        var genericType = typeof(T);
        if (!genericType.IsValueType) return;

        bool isNullable = genericType.IsGenericType &&
                          genericType.GetGenericTypeDefinition() == typeof(Nullable<>);
        if (isNullable) return;

        throw new InvalidCastException(
            $"DelegateCommand<{genericType.Name}> недопустим. " +
            $"Значимые типы нельзя использовать как параметр T, потому что " +
            $"ICommand.CanExecute(null) может вызываться во время инициализации XAML. " +
            $"Используйте DelegateCommand<{genericType.Name}?> вместо этого.");
    }

    /// <summary>
    /// Executes the command through the full pipeline (logging, Before/After, exception handler).
    /// The configured delay is ignored.
    /// </summary>
    public void Execute(T parameter) => ExecuteCore(parameter);

    public bool CanExecute(T parameter) => _canExecuteMethod(parameter);

    protected override void Execute(object? parameter)
    {
        var typed = CommandParameter.Cast<T>(parameter);
        if (_delay.HasValue)
        {
            ExecuteDelayed(typed);
            return;
        }
        ExecuteCore(typed);
    }

    private void ExecuteCore(T parameter)
    {
        try
        {
            Log("Executing command");
            _beforeExecute?.Invoke(parameter);
            _executeMethod(parameter);
            _afterExecute?.Invoke(parameter);
        }
        catch (Exception ex)
        {
            if (!HandleException(ex))
                throw;
        }
    }

    private async void ExecuteDelayed(T parameter)
    {
        await Task.Delay(_delay!.Value);
        if (IsDisposed) return;
        ExecuteCore(parameter);
    }

    protected override bool CanExecute(object? parameter)
        => CommandParameter.TryCast<T>(parameter, out var typed) && _canExecuteMethod(typed);

    #region Fluent API

    public DelegateCommand<T> ObservesCanExecute(Expression<Func<bool>> canExecuteExpression)
    {
        var compiled = canExecuteExpression.Compile();
        _canExecuteMethod = _ => compiled();
        ObservesPropertyInternal(canExecuteExpression);
        return this;
    }

    public DelegateCommand<T> BeforeExecute(Action<T> beforeAction) { _beforeExecute = beforeAction; return this; }
    public DelegateCommand<T> AfterExecute(Action<T> afterAction) { _afterExecute = afterAction; return this; }
    public DelegateCommand<T> When(Func<T, bool> canExecuteMethod)
    {
        _canExecuteMethod = canExecuteMethod ?? throw new ArgumentNullException(nameof(canExecuteMethod));
        return this;
    }

    public static DelegateCommand<T> Create(Action<T> executeMethod) => new(executeMethod);

    #endregion
}
