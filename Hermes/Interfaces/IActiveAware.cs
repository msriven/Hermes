namespace Hermes.Interfaces;

/// <summary>
/// Interface for objects that can be activated/deactivated.
/// </summary>
public interface IActiveAware
{
    /// <summary>
    /// Gets or sets a value indicating whether the object is active.
    /// </summary>
    bool IsActive { get; set; }

    /// <summary>
    /// Notifies that the value for <see cref="IsActive"/> property has changed.
    /// </summary>
    event EventHandler IsActiveChanged;
}
