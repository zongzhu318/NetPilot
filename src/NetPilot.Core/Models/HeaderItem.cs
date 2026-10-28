using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace NetPilot.Core.Models;

public sealed class HeaderItem : INotifyPropertyChanged
{
    private string _key = string.Empty;
    private string _value = string.Empty;
    private bool _enabled = true;

    public string Key
    {
        get => _key;
        set => Set(ref _key, value);
    }

    public string Value
    {
        get => _value;
        set => Set(ref _value, value);
    }

    public bool Enabled
    {
        get => _enabled;
        set => Set(ref _enabled, value);
    }

    public HeaderItem()
    {
    }

    public HeaderItem(string key, string value, bool enabled = true)
    {
        _key = key;
        _value = value;
        _enabled = enabled;
    }

    public HeaderItem Clone() => new(Key, Value, Enabled);

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
