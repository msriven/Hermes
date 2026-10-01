using System.Windows;
using System.Windows.Input;

namespace Hermes;

/*
    <Button Content="Close" Command="{x:Static hermes:WindowCommands.Close}" />

    The target window is resolved automatically, CommandParameter is optional:
      1. CommandParameter is a Window                         -> that window
      2. CommandParameter is a DependencyObject               -> Window.GetWindow(parameter)
      3. otherwise                                            -> active window, then Application.MainWindow

    <Window.InputBindings>
        <KeyBinding Key="Escape" Command="{x:Static hermes:WindowCommands.Close}" />
        <KeyBinding Key="F11"    Command="{x:Static hermes:WindowCommands.ToggleMaximize}" />
    </Window.InputBindings>

    // Dialog buttons
    <Button Content="OK"     Command="{x:Static hermes:WindowCommands.AcceptDialog}" IsDefault="True" />
    <Button Content="Cancel" Command="{x:Static hermes:WindowCommands.CancelDialog}" IsCancel="True" />

    // From code: custom result (null = undecided)
    CloseCommand = WindowCommands.CloseWithResult(null);

    WindowCommands.CloseConfirmationMessage = "Save changes?";
    WindowCommands.CloseConfirmationTitle = "Confirmation";
 */

/// <summary>
/// Static commands for window management, designed for direct XAML binding.
/// </summary>
public static class WindowCommands
{
    public static string CloseConfirmationMessage { get; set; } = "Вы уверены, что хотите закрыть окно?";

    public static string CloseConfirmationTitle { get; set; } = "Подтверждение";

    /// <summary>
    /// Resolves the target window from a command parameter (see the comment at the top of the file).
    /// </summary>
    public static Window? ResolveWindow(object? parameter)
    {
        if (parameter is Window window)
            return window;

        if (parameter is DependencyObject dependencyObject)
        {
            var owner = Window.GetWindow(dependencyObject);
            if (owner != null) return owner;
        }

        var app = Application.Current;
        if (app == null) return null;

        return app.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? app.MainWindow;
    }

    private static DelegateCommand<object> Create(Action<Window> action, Func<Window, bool>? canExecute = null)
        => new(
            parameter =>
            {
                var window = ResolveWindow(parameter);
                if (window != null) action(window);
            },
            parameter =>
            {
                var window = ResolveWindow(parameter);
                return window != null && (canExecute?.Invoke(window) ?? true);
            });

    public static ICommand Close { get; } = Create(w => w.Close());

    public static ICommand Minimize { get; } = Create(
        w => w.WindowState = WindowState.Minimized,
        w => w.ResizeMode != ResizeMode.NoResize);

    public static ICommand Maximize { get; } = Create(
        w => w.WindowState = WindowState.Maximized,
        w => w.ResizeMode is ResizeMode.CanResize or ResizeMode.CanResizeWithGrip);

    /// <summary>Restores a minimized/maximized window to the normal state.</summary>
    public static ICommand Restore { get; } = Create(w => w.WindowState = WindowState.Normal);

    public static ICommand ToggleMaximize { get; } = Create(
        w => w.WindowState = w.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized,
        w => w.ResizeMode is ResizeMode.CanResize or ResizeMode.CanResizeWithGrip);

    public static ICommand Hide { get; } = Create(w => w.Hide());

    public static ICommand Show { get; } = Create(w => w.Show());

    /// <summary>Shows (if needed), restores from minimized state and activates the window.</summary>
    public static ICommand Activate { get; } = Create(w =>
    {
        if (!w.IsVisible) w.Show();
        if (w.WindowState == WindowState.Minimized) w.WindowState = WindowState.Normal;
        w.Activate();
    });

    public static ICommand ToggleTopmost { get; } = Create(w => w.Topmost = !w.Topmost);

    /// <summary>Starts dragging the window (call from a mouse-down handler / EventBinding).</summary>
    public static ICommand DragMove { get; } = Create(w =>
    {
        if (Mouse.LeftButton != MouseButtonState.Pressed) return;
        try { w.DragMove(); }
        catch (InvalidOperationException) { /* button released while starting the drag */ }
    });

    /// <summary>Closes a window shown with ShowDialog() with DialogResult = true (OK).</summary>
    public static ICommand AcceptDialog { get; } = Create(w => CloseDialog(w, true));

    /// <summary>Closes a window shown with ShowDialog() with DialogResult = false (Cancel).</summary>
    public static ICommand CancelDialog { get; } = Create(w => CloseDialog(w, false));

    /// <summary>
    /// Creates a command that closes the window with the given DialogResult
    /// (true / false / null). A window not shown with ShowDialog() is simply closed.
    /// </summary>
    public static ICommand CloseWithResult(bool? result) => Create(w => CloseDialog(w, result));

    /// <summary>Asks for confirmation (see <see cref="CloseConfirmationMessage"/>) and closes the window on Yes.</summary>
    public static ICommand CloseWithConfirmation { get; } = Create(w =>
    {
        var result = MessageBox.Show(
            w,
            CloseConfirmationMessage,
            CloseConfirmationTitle,
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
            w.Close();
    });

    private static void CloseDialog(Window window, bool? result)
    {
        try
        {
            window.DialogResult = result;
        }
        catch (InvalidOperationException)
        {
            // Window was not shown with ShowDialog(): plain close.
            window.Close();
        }
    }
}
