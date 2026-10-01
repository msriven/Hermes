using Hermes.Extensions;
using Hermes.Interfaces;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace Hermes;

/// <summary>
/// Abstract base class for all command implementations.
/// Provides: property/collection observation, IDisposable, CanExecuteChanged (with optional
/// CommandManager integration), logging, delay, error handling and INotifyPropertyChanged.
/// </summary>
public abstract class DelegateCommandBase : ICommand, IActiveAware, INotifyPropertyChanged, IDisposable
{
    private bool _isActive;
    private bool _disposed;
    private bool _hasSubscribers;
    private bool _useCommandManager = true;
    private readonly SynchronizationContext? _synchronizationContext;
    private readonly HashSet<string> _observedKeys = new();
    private readonly List<IDisposable> _subscriptions = new();
    private EventHandler? _canExecuteChangedHandler;

    protected Action<string>? _logger;
    protected TimeSpan? _delay;
    protected Action<Exception>? _exceptionHandler;

    protected DelegateCommandBase()
    {
        _synchronizationContext = SynchronizationContext.Current;
    }

    /// <summary>
    /// Fallback exception handler used when a command has no own handler.
    /// If it is null and the command has no handler, exceptions are re-thrown.
    /// </summary>
    public static Action<Exception>? GlobalExceptionHandler { get; set; }

    /// <summary>Gets a value indicating whether the command has been disposed.</summary>
    protected bool IsDisposed => _disposed;

    #region CanExecuteChanged

    /// <summary>
    /// When CommandManager integration is on (default) the event is forwarded to
    /// CommandManager.RequerySuggested (weak references); otherwise a direct handler list is used.
    /// </summary>
    public virtual event EventHandler? CanExecuteChanged
    {
        add
        {
            _hasSubscribers = true;
            if (_useCommandManager)
                CommandManager.RequerySuggested += value;
            else
                _canExecuteChangedHandler += value;
        }
        remove
        {
            if (_useCommandManager)
                CommandManager.RequerySuggested -= value;
            else
                _canExecuteChangedHandler -= value;
        }
    }

    protected virtual void OnCanExecuteChanged()
    {
        if (_useCommandManager)
        {
            CommandManager.InvalidateRequerySuggested();
            return;
        }

        var handler = _canExecuteChangedHandler;
        if (handler == null) return;

        if (_synchronizationContext != null && _synchronizationContext != SynchronizationContext.Current)
            _synchronizationContext.Post(_ => handler.Invoke(this, EventArgs.Empty), null);
        else
            handler.Invoke(this, EventArgs.Empty);
    }

    [SuppressMessage("Microsoft.Design", "CA1030:UseEventsWhereAppropriate")]
    public void RaiseCanExecuteChanged() => OnCanExecuteChanged();

    #endregion

    void ICommand.Execute(object? parameter) => Execute(parameter);

    bool ICommand.CanExecute(object? parameter) => CanExecute(parameter);

    protected abstract void Execute(object? parameter);

    protected abstract bool CanExecute(object? parameter);

    #region Configuration (used by CommandExtensions)

    internal void ConfigureLogger(Action<string>? logger) => _logger = logger;

    internal void ConfigureDelay(TimeSpan delay) => _delay = delay;

    internal void ConfigureExceptionHandler(Action<Exception>? handler) => _exceptionHandler = handler;

    internal void ConfigureCommandManager(bool use)
    {
        if (_hasSubscribers && use != _useCommandManager)
            throw new InvalidOperationException(
                "UseCommandManager must be configured before the command is bound (CanExecuteChanged already has subscribers).");
        _useCommandManager = use;
    }

    #endregion

    #region Helpers for derived classes

    /// <summary>
    /// Writes a message to the logger (if any), appending the current time.
    /// </summary>
    protected void Log(string message)
    {
        var logger = _logger;
        if (logger != null)
            logger($"{message} at {DateTime.Now}");
    }

    /// <summary>
    /// Passes the exception to the command handler or to <see cref="GlobalExceptionHandler"/>.
    /// Returns false if nobody handled it - the caller must re-throw.
    /// </summary>
    protected bool HandleException(Exception exception)
    {
        var handler = _exceptionHandler;
        if (handler != null)
        {
            handler(exception);
            return true;
        }

        var global = GlobalExceptionHandler;
        if (global != null)
        {
            global(exception);
            return true;
        }

        return false;
    }

    #endregion

    #region Observation

    /// <summary>
    /// Observes a property chain of INotifyPropertyChanged objects and raises CanExecuteChanged on change.
    /// </summary>
    protected internal void ObservesPropertyInternal<T>(Expression<Func<T>> propertyExpression)
    {
        ArgumentNullException.ThrowIfNull(propertyExpression);

        var key = propertyExpression.ToString();
        if (!_observedKeys.Add(key))
            throw new ArgumentException($"{key} is already being observed.", nameof(propertyExpression));

        _subscriptions.Add(PropertyObserver.Observes(propertyExpression, RaiseCanExecuteChanged));
    }

    /// <summary>
    /// Raises CanExecuteChanged whenever the collection changes.
    /// </summary>
    internal void ObservesCollectionInternal(INotifyCollectionChanged collection)
    {
        ArgumentNullException.ThrowIfNull(collection);

        NotifyCollectionChangedEventHandler handler = (_, _) => RaiseCanExecuteChanged();
        collection.CollectionChanged += handler;
        _subscriptions.Add(new ActionDisposable(() => collection.CollectionChanged -= handler));
    }

    private sealed class ActionDisposable : IDisposable
    {
        private Action? _action;
        public ActionDisposable(Action action) => _action = action;
        public void Dispose()
        {
            _action?.Invoke();
            _action = null;
        }
    }

    #endregion

    #region IsActive

    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (_isActive == value) return;
            _isActive = value;
            OnPropertyChanged();
            OnIsActiveChanged();
        }
    }

    public virtual event EventHandler? IsActiveChanged;

    protected virtual void OnIsActiveChanged() => IsActiveChanged?.Invoke(this, EventArgs.Empty);

    #endregion

    #region INotifyPropertyChanged

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    #endregion

    #region IDisposable

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Override to release additional resources. Always call base.Dispose(disposing).</summary>
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;
        if (disposing)
        {
            foreach (var subscription in _subscriptions)
                subscription.Dispose();
            _subscriptions.Clear();
            _observedKeys.Clear();
            _canExecuteChangedHandler = null;
        }
        _disposed = true;
    }

    #endregion
}
