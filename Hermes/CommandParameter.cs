namespace Hermes;

internal static class CommandParameter
{
    /// <summary>
    /// Tries to convert an untyped ICommand parameter. null is accepted only for reference/nullable types.
    /// </summary>
    internal static bool TryCast<T>(object? parameter, out T value)
    {
        if (parameter is null)
        {
            value = default!;
            return default(T) is null;
        }

        if (parameter is T typed)
        {
            value = typed;
            return true;
        }

        value = default!;
        return false;
    }

    internal static T Cast<T>(object? parameter)
    {
        if (TryCast<T>(parameter, out var value))
            return value;

        throw new InvalidCastException(
            $"Cannot use {(parameter is null ? "null" : parameter.GetType().Name)} as command parameter of type '{typeof(T).Name}'. " +
            $"Set CommandParameter or declare the command with a nullable type ({typeof(T).Name}?).");
    }
}
