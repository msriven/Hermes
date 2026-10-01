using System.ComponentModel;
using System.Reflection;

namespace Hermes.Extensions;

/// <summary>
/// Node in a property observation chain. Handles subscription/unsubscription
/// for nested property change notifications.
/// </summary>
public class PropertyObserverNode
{
    private readonly Action _action;
    private INotifyPropertyChanged? _inpcObject;

    public PropertyInfo PropertyInfo { get; }
    public PropertyObserverNode? Next { get; set; }

    public PropertyObserverNode(PropertyInfo propertyInfo, Action action)
    {
        PropertyInfo = propertyInfo ?? throw new ArgumentNullException(nameof(propertyInfo));
        ArgumentNullException.ThrowIfNull(action);

        _action = () =>
        {
            // Re-subscribe the rest of the chain first, so the action sees a consistent state.
            if (Next != null)
            {
                Next.UnsubscribeListener();
                GenerateNextNode();
            }
            action();
        };
    }

    public void SubscribeListenerFor(INotifyPropertyChanged inpcObject)
    {
        UnsubscribeListener();

        _inpcObject = inpcObject;
        _inpcObject.PropertyChanged += OnPropertyChanged;

        if (Next != null) GenerateNextNode();
    }

    /// <summary>Unsubscribes this node and all its children.</summary>
    internal void UnsubscribeListener()
    {
        if (_inpcObject != null)
        {
            _inpcObject.PropertyChanged -= OnPropertyChanged;
            _inpcObject = null;
        }

        Next?.UnsubscribeListener();
    }

    private void GenerateNextNode()
    {
        // Intermediate value may be null or not observable at runtime - then the tail simply stays unsubscribed.
        if (_inpcObject == null || Next == null) return;

        var nextValue = PropertyInfo.GetValue(_inpcObject);
        if (nextValue is INotifyPropertyChanged nextInpc)
            Next.SubscribeListenerFor(nextInpc);
    }

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == PropertyInfo.Name)
            _action();
    }
}
