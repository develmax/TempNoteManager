using System.IO;
using System.Text.Json;
using TempNoteManager.Models;

namespace TempNoteManager.Services;

public sealed class FileStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _storePath;
    private readonly Dictionary<string, FileStateEntry> _entries;

    public FileStateStore()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var directory = Path.Combine(appData, "TempNoteManager");
        _storePath = Path.Combine(directory, "file-state.json");
        _entries = Load();
    }

    public void ApplyToItem(NoteFileItem item)
    {
        var key = GetKey(item);
        if (!_entries.TryGetValue(key, out var entry))
        {
            item.IsLocked = false;
            item.IsFavorite = false;
            return;
        }

        item.IsLocked = entry.IsLocked;
        item.IsFavorite = entry.IsFavorite;
    }

    public void SaveItem(NoteFileItem item)
    {
        var key = GetKey(item);
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        _entries[key] = new FileStateEntry
        {
            Key = key,
            IsLocked = item.IsLocked,
            IsFavorite = item.IsFavorite,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        Save();
    }

    private Dictionary<string, FileStateEntry> Load()
    {
        try
        {
            if (!File.Exists(_storePath))
            {
                return new Dictionary<string, FileStateEntry>(StringComparer.OrdinalIgnoreCase);
            }

            var json = File.ReadAllText(_storePath);
            var entries = JsonSerializer.Deserialize<List<FileStateEntry>>(json, JsonOptions) ?? new List<FileStateEntry>();
            return entries
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Key))
                .GroupBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.OrderByDescending(entry => entry.UpdatedAt).First(), StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, FileStateEntry>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private void Save()
    {
        var directory = Path.GetDirectoryName(_storePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var entries = _entries.Values
            .Where(entry => entry.IsLocked || entry.IsFavorite)
            .OrderByDescending(entry => entry.UpdatedAt)
            .ToList();

        File.WriteAllText(_storePath, JsonSerializer.Serialize(entries, JsonOptions));
    }

    private static string GetKey(NoteFileItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.ContentFingerprint))
        {
            return "sha:" + item.ContentFingerprint.Trim();
        }

        var path = !string.IsNullOrWhiteSpace(item.PathText) ? item.PathText : item.ContentPath;
        return string.IsNullOrWhiteSpace(path) ? string.Empty : "path:" + path.Trim().ToUpperInvariant();
    }
}
