namespace TempNoteManager.Models;

public sealed class AiCacheEntry
{
    public string FileFingerprint { get; set; } = string.Empty;

    public string Summary { get; set; } = string.Empty;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<CachedAiTag> Tags { get; set; } = new();
}

public sealed class CachedAiTag
{
    public string CategoryId { get; set; } = string.Empty;

    public string CategoryName { get; set; } = string.Empty;

    public string CategoryHash { get; set; } = string.Empty;

    public string Color { get; set; } = "#6B7280";

    public string Reason { get; set; } = string.Empty;
}
