namespace TempNoteManager.Models;

public sealed class FileStateEntry
{
    public string Key { get; set; } = string.Empty;

    public bool IsLocked { get; set; }

    public bool IsFavorite { get; set; }

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
