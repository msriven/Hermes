namespace Hermes;

/// <summary>
/// Explicitly named async hooks (aliases of BeforeExecute/AfterExecute, which already take Func&lt;Task&gt;).
/// </summary>
public static class AsyncHookExtensions
{
    public static AsyncDelegateCommand BeforeExecuteAsync(this AsyncDelegateCommand command, Func<Task> hook)
        => command.BeforeExecute(hook);

    public static AsyncDelegateCommand AfterExecuteAsync(this AsyncDelegateCommand command, Func<Task> hook)
        => command.AfterExecute(hook);

    public static AsyncDelegateCommand<T> BeforeExecuteAsync<T>(this AsyncDelegateCommand<T> command, Func<T, Task> hook)
        => command.BeforeExecute(hook);

    public static AsyncDelegateCommand<T> AfterExecuteAsync<T>(this AsyncDelegateCommand<T> command, Func<T, Task> hook)
        => command.AfterExecute(hook);
}
