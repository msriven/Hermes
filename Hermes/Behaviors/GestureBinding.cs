using System.Windows;
using System.Windows.Input;

namespace Hermes.Behaviors;

/*
    xmlns:behaviors="clr-namespace:Hermes.Behaviors;assembly=Hermes"

    // Binds a gesture ("Ctrl+S", "F5", "Ctrl+Shift+N", "LeftDoubleClick", "Ctrl+WheelClick") to a command
    // without writing <KeyBinding> and without a Window.InputBindings block.
    <TextBox behaviors:GestureBinding.Gesture="Ctrl+Enter"
             behaviors:GestureBinding.Command="{Binding SubmitCommand}"
             behaviors:GestureBinding.CommandParameter="{Binding Text, RelativeSource={RelativeSource Self}}" />

    <Border behaviors:GestureBinding.Gesture="LeftDoubleClick"
            behaviors:GestureBinding.Command="{Binding OpenCommand}" />
 */

/// <summary>
/// Attached behavior that adds an <see cref="InputBinding"/> built from a gesture string.
/// Key gestures (KeyGestureConverter) are tried first, then mouse gestures (MouseGestureConverter).
/// Rebuilt whenever Gesture, Command or CommandParameter changes.
/// </summary>
public static class GestureBinding
{
    public static readonly DependencyProperty GestureProperty =
        DependencyProperty.RegisterAttached("Gesture", typeof(string), typeof(GestureBinding),
            new PropertyMetadata(null, OnChanged));

    public static readonly DependencyProperty CommandProperty =
        DependencyProperty.RegisterAttached("Command", typeof(ICommand), typeof(GestureBinding),
            new PropertyMetadata(null, OnChanged));

    public static readonly DependencyProperty CommandParameterProperty =
        DependencyProperty.RegisterAttached("CommandParameter", typeof(object), typeof(GestureBinding),
            new PropertyMetadata(null, OnChanged));

    private static readonly DependencyProperty BindingProperty =
        DependencyProperty.RegisterAttached("Binding", typeof(InputBinding), typeof(GestureBinding));

    public static string? GetGesture(DependencyObject obj) => (string?)obj.GetValue(GestureProperty);
    public static void SetGesture(DependencyObject obj, string? value) => obj.SetValue(GestureProperty, value);

    public static ICommand? GetCommand(DependencyObject obj) => (ICommand?)obj.GetValue(CommandProperty);
    public static void SetCommand(DependencyObject obj, ICommand? value) => obj.SetValue(CommandProperty, value);

    public static object? GetCommandParameter(DependencyObject obj) => obj.GetValue(CommandParameterProperty);
    public static void SetCommandParameter(DependencyObject obj, object? value) => obj.SetValue(CommandParameterProperty, value);

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var bindings = d switch
        {
            UIElement ui => ui.InputBindings,
            ContentElement content => content.InputBindings,
            _ => null
        };
        if (bindings == null) return;

        if (d.GetValue(BindingProperty) is InputBinding previous)
        {
            bindings.Remove(previous);
            d.ClearValue(BindingProperty);
        }

        var command = GetCommand(d);
        var text = GetGesture(d);
        if (command == null || string.IsNullOrWhiteSpace(text)) return;

        var gesture = Parse(text);
        if (gesture == null)
        {
            System.Diagnostics.Debug.WriteLine($"GestureBinding: cannot parse gesture '{text}'.");
            return;
        }

        var binding = new InputBinding(command, gesture) { CommandParameter = GetCommandParameter(d) };
        bindings.Add(binding);
        d.SetValue(BindingProperty, binding);
    }

    private static InputGesture? Parse(string text)
    {
        try
        {
            if (new KeyGestureConverter().ConvertFromInvariantString(text) is InputGesture keyGesture)
                return keyGesture;
        }
        catch (Exception)
        {
            // not a key gesture - try mouse
        }

        try
        {
            return new MouseGestureConverter().ConvertFromInvariantString(text) as InputGesture;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
