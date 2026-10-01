using System.ComponentModel;
using System.Linq.Expressions;
using System.Reflection;

namespace Hermes.Extensions;

/// <summary>
/// Observes property changes on objects implementing INotifyPropertyChanged.
/// Supports chains like () => Obj.Nested.Property and closure-captured locals
/// (() => vm.Prop where vm is a local variable or field).
/// Implements IDisposable to unsubscribe from all PropertyChanged events.
/// </summary>
public class PropertyObserver : IDisposable
{
    private readonly Action _action;
    private PropertyObserverNode? _rootNode;

    private PropertyObserver(Expression propertyExpression, Action action)
    {
        _action = action ?? throw new ArgumentNullException(nameof(action));
        SubscribeListeners(propertyExpression);
    }

    private void SubscribeListeners(Expression propertyExpression)
    {
        // Collect members from the outermost to the root, then reverse to root-first order.
        var members = new List<MemberInfo>();
        var current = propertyExpression;
        while (current is MemberExpression member)
        {
            members.Add(member.Member);
            current = member.Expression!;
        }
        members.Reverse();

        if (current is not ConstantExpression constant)
            throw new NotSupportedException(
                "Unsupported expression. Use a member access chain rooted at 'this' or a captured variable, " +
                "e.g. () => Prop.NestedProp.PropToObserve.");

        // Resolve leading fields (closure display classes, private fields) into the real owner object.
        object? owner = constant.Value;
        int index = 0;
        while (index < members.Count && members[index] is FieldInfo field)
        {
            owner = field.GetValue(owner)
                ?? throw new InvalidOperationException($"Field '{field.Name}' is null; cannot observe properties of null.");
            index++;
        }

        if (index >= members.Count)
            throw new ArgumentException("Expression must end with a property of an INotifyPropertyChanged object.");

        var properties = new List<PropertyInfo>();
        for (int i = index; i < members.Count; i++)
        {
            if (members[i] is not PropertyInfo property)
                throw new NotSupportedException(
                    $"Member '{members[i].Name}' is a field. Only properties can be observed inside the chain.");
            properties.Add(property);
        }

        if (owner is not INotifyPropertyChanged inpcOwner)
            throw new InvalidOperationException(
                $"Trying to subscribe PropertyChanged listener on object that owns '{properties[0].Name}' property, " +
                "but the object does not implement INotifyPropertyChanged.");

        _rootNode = new PropertyObserverNode(properties[0], _action);
        var previous = _rootNode;
        for (int i = 1; i < properties.Count; i++)
        {
            var node = new PropertyObserverNode(properties[i], _action);
            previous.Next = node;
            previous = node;
        }

        _rootNode.SubscribeListenerFor(inpcOwner);
    }

    /// <summary>Unsubscribes from all property change notifications.</summary>
    public void Dispose()
    {
        _rootNode?.UnsubscribeListener();
        _rootNode = null;
    }

    internal static PropertyObserver Observes<T>(Expression<Func<T>> propertyExpression, Action action)
        => new(propertyExpression.Body, action);
}
