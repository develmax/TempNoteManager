using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace TempNoteManager.Models;

public sealed class SuggestedCategory : INotifyPropertyChanged
{
    private bool _isSelected = true;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
            {
                return;
            }

            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string Color { get; set; } = "#2563EB";

    public string Reason { get; set; } = string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;
}
