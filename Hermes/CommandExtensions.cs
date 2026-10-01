using System.Collections.Specialized;
using System.Linq.Expressions;

namespace Hermes;

/// <summary>
/// Fluent configuration shared by all commands. Each method returns the concrete command type,
/// so chains like <c>DelegateCommand.Create(Save).When(...).CatchExceptions(...)</c> keep working.
/// </summary>
public static class CommandExtensions
{
    /// <summary>Raises CanExecuteChanged when the observed property (chain) changes.</summary>
    public static TCommand ObservesProperty<TCommand, TProperty>(this TCommand command, Expression<Func<TProperty>> propertyExpression)
        where TCommand : DelegateCommandBase
    {
        ArgumentNullException.ThrowIfNull(command);
        command.ObservesPropertyInternal(propertyExpression);
        return command;
    }

    /// <summary>Raises CanExecuteChanged when the collection changes (add/remove/reset).</summary>
    public static TCommand ObservesCollection<TCommand>(this TCommand command, INotifyCollectionChanged collection)
        where TCommand : DelegateCommandBase
    {
        ArgumentNullException.ThrowIfNull(command);
        command.ObservesCollectionInternal(collection);
        return command;
    }

    /// <summary>Enables/disables CommandManager.RequerySuggested integration. Call before binding.</summary>
    public static TCommand UseCommandManager<TCommand>(this TCommand command, bool use)
        where TCommand : DelegateCommandBase
    {
        ArgumentNullException.ThrowIfNull(command);
        command.ConfigureCommandManager(use);
        return command;
    }

    /// <summary>Sets an exception handler for the command.</summary>
    public static TCommand CatchExceptions<TCommand>(this TCommand command, Action<Exception> handler)
        where TCommand : DelegateCommandBase
    {
        ArgumentNullException.ThrowIfNull(command);
        command.ConfigureExceptionHandler(handler);
        return command;
    }

    /// <summary>Sets a logging callback.</summary>
    public static TCommand WithLogging<TCommand>(this TCommand command, Action<string> logger)
        where TCommand : DelegateCommandBase
    {
        ArgumentNullException.ThrowIfNull(command);
        command.ConfigureLogger(logger);
        return command;
    }

    /// <summary>Sets a delay before execution.</summary>
    public static TCommand WithDelay<TCommand>(this TCommand command, TimeSpan delay)
        where TCommand : DelegateCommandBase
    {
        ArgumentNullException.ThrowIfNull(command);
        command.ConfigureDelay(delay);
        return command;
    }
}
