using System.IO;
using System.Text.Json;
using TempNoteManager.Models;

namespace TempNoteManager.Services;

public sealed class AiAnalysisCacheStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _cachePath;
    private readonly Dictionary<string, AiCacheEntry> _entries;

    public AiAnalysisCacheStore()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var directory = Path.Combine(appData, "TempNoteManager");
        _cachePath = Path.Combine(directory, "ai-cache.json");
        _entries = LoadEntries();
    }

    public void ApplyToItem(NoteFileItem item, IReadOnlyCollection<StorageCategory> categories)
    {
        if (string.IsNullOrWhiteSpace(item.ContentFingerprint)
            || !_entries.TryGetValue(item.ContentFingerprint, out var entry))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(entry.Summary))
        {
            item.Summary = entry.Summary;
            item.SummaryState = string.Empty;
        }

        var tags = entry.Tags
            .Select(tag => CreateCurrentTag(tag, categories))
            .Where(tag => tag is not null)
            .Cast<NoteTag>()
            .ToList();

        item.SetTags(tags);
    }

    public IReadOnlyList<StorageCategory> GetCategoriesNeedingClassification(
        NoteFileItem item,
        IReadOnlyCollection<StorageCategory> categories)
    {
        if (string.IsNullOrWhiteSpace(item.ContentFingerprint)
            || !_entries.TryGetValue(item.ContentFingerprint, out var entry))
        {
            return categories.ToList();
        }

        return categories
            .Where(category => !HasFreshCategoryResult(entry, category))
            .ToList();
    }

    public void SaveSummary(NoteFileItem item)
    {
        if (string.IsNullOrWhiteSpace(item.ContentFingerprint))
        {
            return;
        }

        var entry = GetOrCreateEntry(item.ContentFingerprint);
        entry.Summary = item.Summary;
        entry.UpdatedAt = DateTimeOffset.UtcNow;
        Save();
    }

    public void SaveClassification(
        NoteFileItem item,
        IReadOnlyCollection<StorageCategory> analyzedCategories,
        IReadOnlyCollection<NoteTag> returnedTags,
        IReadOnlyCollection<StorageCategory> allCategories)
    {
        if (string.IsNullOrWhiteSpace(item.ContentFingerprint))
        {
            return;
        }

        var entry = GetOrCreateEntry(item.ContentFingerprint);
        var analyzedIds = analyzedCategories.Select(category => category.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        entry.Tags.RemoveAll(tag => analyzedIds.Contains(tag.CategoryId));

        foreach (var category in analyzedCategories)
        {
            var returnedTag = returnedTags.FirstOrDefault(tag => tag.Name.Equals(category.Name, StringComparison.OrdinalIgnoreCase));
            if (returnedTag is null)
            {
                entry.Tags.Add(new CachedAiTag
                {
                    CategoryId = category.Id,
                    CategoryName = category.Name,
                    CategoryHash = GetCategoryHash(category),
                    Color = category.Color,
                    Reason = string.Empty
                });
                continue;
            }

            entry.Tags.Add(new CachedAiTag
            {
                CategoryId = category.Id,
                CategoryName = category.Name,
                CategoryHash = GetCategoryHash(category),
                Color = category.Color,
                Reason = returnedTag.Reason
            });
        }

        entry.UpdatedAt = DateTimeOffset.UtcNow;
        Save();
        ApplyToItem(item, allCategories);
    }

    public static string GetCategoryHash(StorageCategory category)
    {
        return FileFingerprintService.ComputeCategoryHash(category.Name, category.Description);
    }

    private static NoteTag? CreateCurrentTag(CachedAiTag cachedTag, IReadOnlyCollection<StorageCategory> categories)
    {
        var category = categories.FirstOrDefault(candidate => candidate.Id.Equals(cachedTag.CategoryId, StringComparison.OrdinalIgnoreCase));
        if (category is null)
        {
            return null;
        }

        if (!cachedTag.CategoryHash.Equals(GetCategoryHash(category), StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(cachedTag.Reason))
        {
            return null;
        }

        return new NoteTag
        {
            Name = category.Name,
            Color = category.Color,
            Reason = cachedTag.Reason
        };
    }

    private static bool HasFreshCategoryResult(AiCacheEntry entry, StorageCategory category)
    {
        var hash = GetCategoryHash(category);
        return entry.Tags.Any(tag =>
            tag.CategoryId.Equals(category.Id, StringComparison.OrdinalIgnoreCase)
            && tag.CategoryHash.Equals(hash, StringComparison.OrdinalIgnoreCase));
    }

    private AiCacheEntry GetOrCreateEntry(string fingerprint)
    {
        if (_entries.TryGetValue(fingerprint, out var entry))
        {
            return entry;
        }

        entry = new AiCacheEntry
        {
            FileFingerprint = fingerprint
        };
        _entries[fingerprint] = entry;
        return entry;
    }

    private Dictionary<string, AiCacheEntry> LoadEntries()
    {
        try
        {
            if (!File.Exists(_cachePath))
            {
                return new Dictionary<string, AiCacheEntry>(StringComparer.OrdinalIgnoreCase);
            }

            var json = File.ReadAllText(_cachePath);
            var entries = JsonSerializer.Deserialize<List<AiCacheEntry>>(json, JsonOptions) ?? [];
            return entries
                .Where(entry => !string.IsNullOrWhiteSpace(entry.FileFingerprint))
                .GroupBy(entry => entry.FileFingerprint, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, AiCacheEntry>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private void Save()
    {
        var directory = Path.GetDirectoryName(_cachePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var entries = _entries.Values
            .OrderByDescending(entry => entry.UpdatedAt)
            .Take(5000)
            .ToList();

        File.WriteAllText(_cachePath, JsonSerializer.Serialize(entries, JsonOptions));
    }
}
