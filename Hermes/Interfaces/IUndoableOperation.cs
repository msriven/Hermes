namespace Hermes.Interfaces;

/// <summary>
/// A reversible operation used by <see cref="UndoRedoManager"/>.
/// </summary>
public interface IUndoableOperation
{
    /// <summary>Optional human readable description (for menus, tooltips).</summary>
    string? Description { get; }

    void Execute();

    void Undo();
}
