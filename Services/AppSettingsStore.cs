using System.IO;
using System.Text.Json;
using TempNoteManager.Models;

namespace TempNoteManager.Services;

public sealed class AppSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _settingsPath;
    private readonly WindowsCredentialStore _credentialStore = new();

    public AppSettingsStore()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var directory = Path.Combine(appData, "TempNoteManager");
        _settingsPath = Path.Combine(directory, "settings.json");
    }

    public AppSettings Load()
    {
        var settings = TryReadSettings() ?? new AppSettings();

        if (string.IsNullOrWhiteSpace(settings.SessionPath))
        {
            settings.SessionPath = NotepadPlusPlusSessionService.GetDefaultSessionPath();
        }

        if (string.IsNullOrWhiteSpace(settings.CategoryRootPath))
        {
            var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            settings.CategoryRootPath = Path.Combine(documents, "TempNoteManager Categories");
        }

        if (string.IsNullOrWhiteSpace(settings.TrashFolderPath))
        {
            settings.TrashFolderPath = Path.Combine(settings.CategoryRootPath, "_Trash");
        }

        if (string.IsNullOrWhiteSpace(settings.TrashMode))
        {
            settings.TrashMode = TrashModes.RecycleBin;
        }

        foreach (var category in settings.Categories)
        {
            if (string.IsNullOrWhiteSpace(category.Id))
            {
                category.Id = Guid.NewGuid().ToString("N");
            }

            if (string.IsNullOrWhiteSpace(category.Color))
            {
                category.Color = "#2563EB";
            }

            if (string.IsNullOrWhiteSpace(category.DirectoryPath) && !string.IsNullOrWhiteSpace(category.Name))
            {
                category.DirectoryPath = Path.Combine(settings.CategoryRootPath, SanitizeDirectoryName(category.Name));
            }
        }

        settings.AiApiKey =
            Environment.GetEnvironmentVariable("TEMP_NOTE_AI_API_KEY")
            ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY")
            ?? _credentialStore.ReadApiKey()
            ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TEMP_NOTE_AI_ENDPOINT")))
        {
            settings.AiEndpoint = Environment.GetEnvironmentVariable("TEMP_NOTE_AI_ENDPOINT")!;
        }

        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TEMP_NOTE_AI_MODEL")))
        {
            settings.AiModel = Environment.GetEnvironmentVariable("TEMP_NOTE_AI_MODEL")!;
        }

        return settings;
    }

    public void Save(AppSettings settings)
    {
        var directory = Path.GetDirectoryName(_settingsPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(_settingsPath, JsonSerializer.Serialize(settings, JsonOptions));
    }

    public void SaveAiApiKey(string apiKey)
    {
        _credentialStore.SaveApiKey(apiKey);
    }

    public void DeleteAiApiKey()
    {
        _credentialStore.DeleteApiKey();
    }

    private AppSettings? TryReadSettings()
    {
        try
        {
            if (!File.Exists(_settingsPath))
            {
                return null;
            }

            var json = File.ReadAllText(_settingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static string SanitizeDirectoryName(string name)
    {
        var result = string.IsNullOrWhiteSpace(name) ? "Category" : name.Trim();

        foreach (var invalidChar in Path.GetInvalidFileNameChars())
        {
            result = result.Replace(invalidChar, '_');
        }

        return result;
    }
}
