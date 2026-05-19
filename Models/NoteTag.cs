using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace TempNoteManager.Models;

public sealed class NoteTag : INotifyPropertyChanged
{
    private string _name = string.Empty;
    private string _color = "#6B7280";
    private string _reason = string.Empty;

    public string Name
    {
        get => _name;
        set => SetField(ref _name, value);
    }

    public string Color
    {
        get => _color;
        set => SetField(ref _color, value);
    }

    public string Reason
    {
        get => _reason;
        set => SetField(ref _reason, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}
