using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;

namespace Hermes.Behaviors;

/*
    xmlns:hermes="clr-namespace:Hermes.Behaviors;assembly=Hermes"

    <TextBox hermes:EventBinding.EventName="TextChanged"
             hermes:EventBinding.Command="{Binding SearchCommand}" />

    // Only on Enter (also: Modifiers="Control")
    <TextBox hermes:EventBinding.EventName="KeyDown"
             hermes:EventBinding.Key="Enter"
             hermes:EventBinding.Command="{Binding SubmitCommand}" />

    // Sender / EventArgs / converted EventArgs as the command parameter
    <Button hermes:EventBinding.EventName="Click" hermes:EventBinding.PassSender="True" hermes:EventBinding.Command="{Binding ClickedCommand}" />
    <ListView hermes:EventBinding.EventName="SelectionChanged" hermes:EventBinding.PassEventArgs="True" hermes:EventBinding.Command="{Binding SelectionCommand}" />
    <ListView hermes:EventBinding.EventName="MouseDoubleClick"
              hermes:EventBinding.EventArgsConverter="{StaticResource MouseArgsToItem}"
              hermes:EventBinding.Command="{Binding OpenCommand}" />

    // Custom filter: class implements IEventFilter
    <Grid hermes:EventBinding.EventName="MouseDown" hermes:EventBinding.Filter="{StaticResource RightButtonOnly}" ... />

    // Routed event by name (when there is no CLR wrapper), "Owner.Name" disambiguates; HandledEventsToo for handled events
    <Border hermes:EventBinding.EventName="ButtonBase.Click" hermes:EventBinding.HandledEventsToo="True" ... />

    // Mark the routed event as handled
    <TextBox hermes:EventBinding.EventName="PreviewKeyDown" hermes:EventBinding.MarkHandled="True" ... />
 */

/// <summary>
/// Decides whether an event should invoke the command.
/// </summary>
public interface IEventFilter
{
    bool ShouldExecute(object? sender, EventArgs args);
}

/// <summary>
/// Attached behavior that binds a CLR event (or a routed event by name) to an ICommand.
/// No external dependencies. Command, CommandParameter and options are read at event time,
/// so they can change (or be bound later) without re-subscribing.
/// Parameter priority: EventArgsConverter > PassEventArgs > PassSender > CommandParameter.
/// </summary>
public static class EventBinding
{
    #region Attached Properties

    public static readonly DependencyProperty EventNameProperty =
        DependencyProperty.RegisterAttached("EventName", typeof(string), typeof(EventBinding),
            new PropertyMetadata(null, OnEventNameChanged));

    public static readonly DependencyProperty CommandProperty =
        DependencyProperty.RegisterAttached("Command", typeof(ICommand), typeof(EventBinding),
            new PropertyMetadata(null, OnCommandChanged));

    public static readonly DependencyProperty CommandParameterProperty =
        DependencyProperty.RegisterAttached("CommandParameter", typeof(object), typeof(EventBinding),
            new PropertyMetadata(null));

    public static readonly DependencyProperty PassEventArgsProperty =
        DependencyProperty.RegisterAttached("PassEventArgs", typeof(bool), typeof(EventBinding),
            new PropertyMetadata(false));

    public static readonly DependencyProperty PassSenderProperty =
        DependencyProperty.RegisterAttached("PassSender", typeof(bool), typeof(EventBinding),
            new PropertyMetadata(false));

    public static readonly DependencyProperty EventArgsConverterProperty =
        DependencyProperty.RegisterAttached("EventArgsConverter", typeof(IValueConverter), typeof(EventBinding),
            new PropertyMetadata(null));

    public static readonly DependencyProperty FilterProperty =
        DependencyProperty.RegisterAttached("Filter", typeof(IEventFilter), typeof(EventBinding),
            new PropertyMetadata(null));

    public static readonly DependencyProperty KeyProperty =
        DependencyProperty.RegisterAttached("Key", typeof(Key), typeof(EventBinding),
            new PropertyMetadata(Key.None));

    public static readonly DependencyProperty ModifiersProperty =
        DependencyProperty.RegisterAttached("Modifiers", typeof(ModifierKeys), typeof(EventBinding),
            new PropertyMetadata(ModifierKeys.None));

    public static readonly DependencyProperty MarkHandledProperty =
        DependencyProperty.RegisterAttached("MarkHandled", typeof(bool), typeof(EventBinding),
            new PropertyMetadata(false));

    public static readonly DependencyProperty HandledEventsTooProperty =
        DependencyProperty.RegisterAttached("HandledEventsToo", typeof(bool), typeof(EventBinding),
            new PropertyMetadata(false, OnEventNameChanged));

    private static readonly DependencyProperty SubscriptionProperty =
        DependencyProperty.RegisterAttached("Subscription", typeof(Subscription), typeof(EventBinding));

    #endregion

    #region Getters/Setters

    public static string GetEventName(DependencyObject obj) => (string)obj.GetValue(EventNameProperty);
    public static void SetEventName(DependencyObject obj, string value) => obj.SetValue(EventNameProperty, value);

    public static ICommand GetCommand(DependencyObject obj) => (ICommand)obj.GetValue(CommandProperty);
    public static void SetCommand(DependencyObject obj, ICommand value) => obj.SetValue(CommandProperty, value);

    public static object GetCommandParameter(DependencyObject obj) => obj.GetValue(CommandParameterProperty);
    public static void SetCommandParameter(DependencyObject obj, object value) => obj.SetValue(CommandParameterProperty, value);

    public static bool GetPassEventArgs(DependencyObject obj) => (bool)obj.GetValue(PassEventArgsProperty);
    public static void SetPassEventArgs(DependencyObject obj, bool value) => obj.SetValue(PassEventArgsProperty, value);

    public static bool GetPassSender(DependencyObject obj) => (bool)obj.GetValue(PassSenderProperty);
    public static void SetPassSender(DependencyObject obj, bool value) => obj.SetValue(PassSenderProperty, value);

    public static IValueConverter? GetEventArgsConverter(DependencyObject obj) => (IValueConverter?)obj.GetValue(EventArgsConverterProperty);
    public static void SetEventArgsConverter(DependencyObject obj, IValueConverter? value) => obj.SetValue(EventArgsConverterProperty, value);

    public static IEventFilter? GetFilter(DependencyObject obj) => (IEventFilter?)obj.GetValue(FilterProperty);
    public static void SetFilter(DependencyObject obj, IEventFilter? value) => obj.SetValue(FilterProperty, value);

    public static Key GetKey(DependencyObject obj) => (Key)obj.GetValue(KeyProperty);
    public static void SetKey(DependencyObject obj, Key value) => obj.SetValue(KeyProperty, value);

    public static ModifierKeys GetModifiers(DependencyObject obj) => (ModifierKeys)obj.GetValue(ModifiersProperty);
    public static void SetModifiers(DependencyObject obj, ModifierKeys value) => obj.SetValue(ModifiersProperty, value);

    public static bool GetMarkHandled(DependencyObject obj) => (bool)obj.GetValue(MarkHandledProperty);
    public static void SetMarkHandled(DependencyObject obj, bool value) => obj.SetValue(MarkHandledProperty, value);

    public static bool GetHandledEventsToo(DependencyObject obj) => (bool)obj.GetValue(HandledEventsTooProperty);
    public static void SetHandledEventsToo(DependencyObject obj, bool value) => obj.SetValue(HandledEventsTooProperty, value);

    #endregion

    private static void OnEventNameChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        Detach(d);
        Attach(d);
    }

    private static void OnCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        // The handler reads the command dynamically; only subscribe if not yet subscribed.
        if (d.GetValue(SubscriptionProperty) == null)
            Attach(d);
    }

    private static void Detach(DependencyObject element)
    {
        if (element.GetValue(SubscriptionProperty) is Subscription subscription)
        {
            subscription.Unsubscribe();
            element.ClearValue(SubscriptionProperty);
        }
    }

    private static void Attach(DependencyObject element)
    {
        var eventName = GetEventName(element);
        if (string.IsNullOrEmpty(eventName)) return;

        var adapter = new Adapter(element);
        var method = typeof(Adapter).GetMethod(nameof(Adapter.Handle))!;

        // 1) CLR event
        var eventInfo = element.GetType().GetEvent(eventName);
        if (eventInfo?.EventHandlerType != null)
        {
            var handler = Delegate.CreateDelegate(eventInfo.EventHandlerType, adapter, method, throwOnBindFailure: false);
            if (handler == null)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"EventBinding: event '{eventName}' has an unsupported handler signature (expected (object, EventArgs)).");
                return;
            }

            eventInfo.AddEventHandler(element, handler);
            element.SetValue(SubscriptionProperty, new Subscription(() => eventInfo.RemoveEventHandler(element, handler)));
            return;
        }

        // 2) Routed event by name ("Click" or "Owner.Click")
        var routedEvent = FindRoutedEvent(element.GetType(), eventName);
        if (routedEvent != null)
        {
            var handler = Delegate.CreateDelegate(routedEvent.HandlerType, adapter, method, throwOnBindFailure: false);
            bool handledToo = GetHandledEventsToo(element);

            if (handler != null && element is UIElement ui)
            {
                ui.AddHandler(routedEvent, handler, handledToo);
                element.SetValue(SubscriptionProperty, new Subscription(() => ui.RemoveHandler(routedEvent, handler)));
                return;
            }

            if (handler != null && element is ContentElement content)
            {
                content.AddHandler(routedEvent, handler, handledToo);
                element.SetValue(SubscriptionProperty, new Subscription(() => content.RemoveHandler(routedEvent, handler)));
                return;
            }
        }

        System.Diagnostics.Debug.WriteLine($"EventBinding: event '{eventName}' not found on {element.GetType().Name}.");
    }

    private static RoutedEvent? FindRoutedEvent(Type elementType, string eventName)
    {
        string? ownerName = null;
        string name = eventName;
        int dot = eventName.LastIndexOf('.');
        if (dot > 0)
        {
            ownerName = eventName[..dot];
            name = eventName[(dot + 1)..];
        }

        return EventManager.GetRoutedEvents().FirstOrDefault(e =>
            e.Name == name &&
            (ownerName == null ? e.OwnerType.IsAssignableFrom(elementType) : e.OwnerType.Name == ownerName));
    }

    private sealed record Subscription(Action Unsubscribe);

    /// <summary>
    /// Target of the generated delegate. Parameter contravariance lets one (object, EventArgs)
    /// method serve any standard handler type without compiling expression trees.
    /// </summary>
    private sealed class Adapter
    {
        private readonly DependencyObject _element;

        public Adapter(DependencyObject element) => _element = element;

        public void Handle(object? sender, EventArgs args)
        {
            var command = GetCommand(_element);
            if (command == null) return;

            if (!PassesFilters(sender, args)) return;

            var parameter = GetCommandParameter(_element);
            var converter = GetEventArgsConverter(_element);

            if (converter != null)
                parameter = converter.Convert(args, typeof(object), parameter, CultureInfo.CurrentCulture)!;
            else if (GetPassEventArgs(_element))
                parameter = args;
            else if (GetPassSender(_element))
                parameter = sender!;

            if (!command.CanExecute(parameter)) return;

            command.Execute(parameter);
            if (GetMarkHandled(_element) && args is RoutedEventArgs routed)
                routed.Handled = true;
        }

        private bool PassesFilters(object? sender, EventArgs args)
        {
            var key = GetKey(_element);
            if (key != Key.None)
            {
                if (args is not KeyEventArgs keyArgs) return false;
                var actual = keyArgs.Key == Key.System ? keyArgs.SystemKey : keyArgs.Key;
                if (actual != key) return false;
            }

            var modifiers = GetModifiers(_element);
            if (modifiers != ModifierKeys.None && Keyboard.Modifiers != modifiers)
                return false;

            var filter = GetFilter(_element);
            return filter == null || filter.ShouldExecute(sender, args);
        }
    }
}
