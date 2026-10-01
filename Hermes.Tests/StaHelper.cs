using System.Runtime.ExceptionServices;

namespace Hermes.Tests;

internal static class Sta
{
    /// <summary>Runs the action on an STA thread (required for WPF elements) and re-throws its exception.</summary>
    public static void Run(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (error != null)
            ExceptionDispatchInfo.Capture(error).Throw();
    }
}
