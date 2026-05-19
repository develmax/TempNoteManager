using System.Text.Json.Serialization;

namespace TempNoteManager.Models;

public sealed class AppSettings
{
    public string SessionPath { get; set; } = string.Empty;

    public bool AiEnabled { get; set; }

    public string AiEndpoint { get; set; } = "https://api.openai.com/v1/chat/completions";

    public string AiModel { get; set; } = "gpt-4.1-mini";

    public string LastSaveFolder { get; set; } = string.Empty;

    public string TrashMode { get; set; } = TrashModes.RecycleBin;

    public string TrashFolderPath { get; set; } = string.Empty;

    public string CategoryRootPath { get; set; } = string.Empty;

    public List<StorageCategory> Categories { get; set; } = new();

    [JsonIgnore]
    public string AiApiKey { get; set; } = string.Empty;
}
