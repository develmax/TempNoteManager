using System.ComponentModel;
using System.Collections.ObjectModel;
using System.IO;
using System.Runtime.CompilerServices;

namespace TempNoteManager.Models;

public sealed class NoteFileItem : INotifyPropertyChanged
{
    private string _contentPath = string.Empty;
    private string _displayName = string.Empty;
    private string _fileName = string.Empty;
    private string _backupPath = string.Empty;
    private string _originalPath = string.Empty;
    private bool _isTemporary;
    private bool _isInTemporaryStorage;
    private bool _exists;
    private DateTime? _createdAt;
    private DateTime? _modifiedAt;
    private long _sizeBytes;
    private string _previewText = string.Empty;
    private string _fullContent = string.Empty;
    private string _summary = string.Empty;
    private string _summaryState = string.Empty;
    private string _categoryName = string.Empty;
    private string _categoryColor = string.Empty;
    private string _contentFingerprint = string.Empty;
    private bool _isSummaryLoading;

    public string SessionKey { get; init; } = string.Empty;

    public string SessionPath { get; init; } = string.Empty;

    public string FileName
    {
        get => _fileName;
        set => SetField(ref _fileName, value);
    }

    public string BackupPath
    {
        get => _backupPath;
        set => SetField(ref _backupPath, value);
    }

    public string OriginalPath
    {
        get => _originalPath;
        set
        {
            if (SetField(ref _originalPath, value))
            {
                OnPropertyChanged(nameof(PathText));
            }
        }
    }

    public string ContentPath
    {
        get => _contentPath;
        set
        {
            if (SetField(ref _contentPath, value))
            {
                OnPropertyChanged(nameof(PathText));
            }
        }
    }

    public string DisplayName
    {
        get => _displayName;
        set => SetField(ref _displayName, value);
    }

    public bool IsTemporary
    {
        get => _isTemporary;
        set
        {
            if (SetField(ref _isTemporary, value))
            {
                OnPropertyChanged(nameof(StorageText));
                OnPropertyChanged(nameof(CanSaveAsPermanent));
                OnPropertyChanged(nameof(CanConvertToTemporary));
            }
        }
    }

    public bool IsInTemporaryStorage
    {
        get => _isInTemporaryStorage;
        set
        {
            if (SetField(ref _isInTemporaryStorage, value))
            {
                OnPropertyChanged(nameof(StorageText));
            }
        }
    }

    public bool Exists
    {
        get => _exists;
        set
        {
            if (SetField(ref _exists, value))
            {
                OnPropertyChanged(nameof(StatusText));
            }
        }
    }

    public DateTime? CreatedAt
    {
        get => _createdAt;
        set
        {
            if (SetField(ref _createdAt, value))
            {
                OnPropertyChanged(nameof(CreatedText));
            }
        }
    }

    public DateTime? ModifiedAt
    {
        get => _modifiedAt;
        set
        {
            if (SetField(ref _modifiedAt, value))
            {
                OnPropertyChanged(nameof(ModifiedText));
            }
        }
    }

    public long SizeBytes
    {
        get => _sizeBytes;
        set
        {
            if (SetField(ref _sizeBytes, value))
            {
                OnPropertyChanged(nameof(SizeText));
            }
        }
    }

    public string Language { get; init; } = string.Empty;

    public string EncodingHint { get; init; } = string.Empty;

    public string PreviewText
    {
        get => _previewText;
        set => SetField(ref _previewText, value);
    }

    public string FullContent
    {
        get => _fullContent;
        set => SetField(ref _fullContent, value);
    }

    public string Summary
    {
        get => _summary;
        set => SetField(ref _summary, value);
    }

    public string SummaryState
    {
        get => _summaryState;
        set => SetField(ref _summaryState, value);
    }

    public bool IsSummaryLoading
    {
        get => _isSummaryLoading;
        set => SetField(ref _isSummaryLoading, value);
    }

    public string CategoryName
    {
        get => _categoryName;
        set
        {
            if (SetField(ref _categoryName, value))
            {
                OnPropertyChanged(nameof(CategoryText));
                OnPropertyChanged(nameof(IsCategorized));
            }
        }
    }

    public string CategoryColor
    {
        get => _categoryColor;
        set => SetField(ref _categoryColor, value);
    }

    public ObservableCollection<NoteTag> Tags { get; } = new();

    public string ContentFingerprint
    {
        get => _contentFingerprint;
        set => SetField(ref _contentFingerprint, value);
    }

    public bool CanSaveAsPermanent => IsTemporary && Exists;

    public bool CanConvertToTemporary => !IsTemporary && Exists;

    public bool IsCategorized => !string.IsNullOrWhiteSpace(CategoryName);

    public string CategoryText => IsCategorized ? CategoryName : "Общий";

    public string TagsText => Tags.Count == 0 ? string.Empty : string.Join(", ", Tags.Select(tag => tag.Name));

    public string PrimaryTagColor => Tags.Count == 0 ? "#EEF2F7" : Tags[0].Color;

    public string StatusText => Exists ? "Доступен" : "Файл не найден";

    public string StorageText
    {
        get
        {
            if (IsTemporary)
            {
                return "Временный";
            }

            return IsInTemporaryStorage ? "Постоянный + snapshot" : "Постоянный";
        }
    }

    public string CreatedText => CreatedAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? "-";

    public string ModifiedText => ModifiedAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? "-";

    public string SizeText
    {
        get
        {
            if (SizeBytes <= 0)
            {
                return Exists ? "0 Б" : "-";
            }

            string[] units = ["Б", "КБ", "МБ", "ГБ"];
            var value = (double)SizeBytes;
            var index = 0;

            while (value >= 1024 && index < units.Length - 1)
            {
                value /= 1024;
                index++;
            }

            return $"{value:0.#} {units[index]}";
        }
    }

    public string PathText
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(OriginalPath) && Path.IsPathRooted(OriginalPath))
            {
                return OriginalPath;
            }

            return ContentPath;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void ClearLoadedContent()
    {
        FullContent = string.Empty;
    }

    public void RefreshDerivedProperties()
    {
        OnPropertyChanged(nameof(StorageText));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(CreatedText));
        OnPropertyChanged(nameof(ModifiedText));
        OnPropertyChanged(nameof(SizeText));
        OnPropertyChanged(nameof(PathText));
        OnPropertyChanged(nameof(CategoryText));
        OnPropertyChanged(nameof(TagsText));
        OnPropertyChanged(nameof(PrimaryTagColor));
        OnPropertyChanged(nameof(CanSaveAsPermanent));
        OnPropertyChanged(nameof(CanConvertToTemporary));
    }

    public void SetTags(IEnumerable<NoteTag> tags)
    {
        Tags.Clear();
        foreach (var tag in tags)
        {
            Tags.Add(tag);
        }

        OnPropertyChanged(nameof(Tags));
        OnPropertyChanged(nameof(TagsText));
        OnPropertyChanged(nameof(PrimaryTagColor));
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
