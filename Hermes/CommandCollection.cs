using System.Collections.ObjectModel;
using System.Windows.Input;
using Hermes.Interfaces;

namespace Hermes;

/// <summary>
/// Thread-safe registry of child commands shared by all composite commands.
/// Re-raises the owner's CanExecuteChanged when any child changes (or becomes active/inactive).
/// </summary>
internal sealed class CommandCollection
{
    private readonly List<ICommand> _commands = new();
    private readonly object _lock = new();
    private readonly Action _changed;

    public CommandCollection(Action changed) => _changed = changed;

    /// <summary>When true, commands implementing IActiveAware with IsActive == false are ignored.</summary>
    public bool RespectIsActive { get; set; }

    public bool Add(ICommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        lock (_lock)
        {
            if (_commands.Contains(command)) return false;
            _commands.Add(command);
            command.CanExecuteChanged += OnChanged;
            if (command is IActiveAware active)
                active.IsActiveChanged += OnChanged;
        }
        _changed();
        return true;
    }

    public bool Remove(ICommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        bool removed;
        lock (_lock)
        {
            removed = _commands.Remove(command);
            if (removed) Detach(command);
        }
        if (removed) _changed();
        return removed;
    }

    public void Clear()
    {
        lock (_lock)
        {
            foreach (var command in _commands)
                Detach(command);
            _commands.Clear();
        }
    }

    public ReadOnlyCollection<ICommand> All()
    {
        lock (_lock)
            return new List<ICommand>(_commands).AsReadOnly();
    }

    /// <summary>Snapshot of participating commands (filtered by IsActive if requested).</summary>
    public List<ICommand> Snapshot()
    {
        lock (_lock)
        {
            if (!RespectIsActive)
                return new List<ICommand>(_commands);

            return _commands.Where(c => c is not IActiveAware { IsActive: false }).ToList();
        }
    }

    private void Detach(ICommand command)
    {
        command.CanExecuteChanged -= OnChanged;
        if (command is IActiveAware active)
            active.IsActiveChanged -= OnChanged;
    }

    private void OnChanged(object? sender, EventArgs e) => _changed();
}
