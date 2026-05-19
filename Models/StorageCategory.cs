using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json.Serialization;

namespace TempNoteManager.Models;

public sealed class StorageCategory : INotifyPropertyChanged
{
    private string _name = string.Empty;
    private string _description = string.Empty;
    private string _directoryPath = string.Empty;
    private string _color = "#2563EB";

    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name
    {
        get => _name;
        set
        {
            if (_name == value)
            {
                return;
            }

            _name = value;
            OnPropertyChanged(nameof(Name));
            OnPropertyChanged(nameof(DisplayTitle));
        }
    }

    public string Description
    {
        get => _description;
        set
        {
            if (_description == value)
            {
                return;
            }

            _description = value;
            OnPropertyChanged(nameof(Description));
        }
    }

    public string DirectoryPath
    {
        get => _directoryPath;
        set
        {
            if (_directoryPath == value)
            {
                return;
            }

            _directoryPath = value;
            OnPropertyChanged(nameof(DirectoryPath));
        }
    }

    public string Color
    {
        get => _color;
        set
        {
            if (_color == value)
            {
                return;
            }

            _color = value;
            OnPropertyChanged(nameof(Color));
        }
    }

    [JsonIgnore]
    public ObservableCollection<NoteFileItem> Items { get; } = new();

    [JsonIgnore]
    public string DisplayTitle => $"{Name} ({Items.Count})";

    public event PropertyChangedEventHandler? PropertyChanged;

    public void NotifyItemsChanged()
    {
        OnPropertyChanged(nameof(DisplayTitle));
    }

    private void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
