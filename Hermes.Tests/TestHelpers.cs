using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Hermes.Tests;

public class TestViewModel : INotifyPropertyChanged
{
    private bool _flag;
    private TestViewModel? _nested;
    private int _value;

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool Flag { get => _flag; set => Set(ref _flag, value); }
    public int Value { get => _value; set => Set(ref _value, value); }
    public TestViewModel? Nested { get => _nested; set => Set(ref _nested, value); }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
