using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace TempNoteManager.Services;

public sealed class UpdateCheckService
{
    private const string LatestReleaseUrl = "https://api.github.com/repos/develmax/TempNoteManager/releases/latest";

    public async Task<UpdateCheckResult> CheckLatestAsync(CancellationToken cancellationToken = default)
    {
        using var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("TempNoteManager");

        using var response = await client.GetAsync(LatestReleaseUrl, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;
        var tag = root.TryGetProperty("tag_name", out var tagElement) ? tagElement.GetString() ?? string.Empty : string.Empty;
        var url = root.TryGetProperty("html_url", out var urlElement) ? urlElement.GetString() ?? string.Empty : string.Empty;
        var current = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "dev";

        return new UpdateCheckResult
        {
            CurrentVersion = current,
            LatestTag = tag,
            ReleaseUrl = url,
            IsUpdateAvailable = IsDifferentVersion(current, tag)
        };
    }

    public static void OpenReleasePage(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        Process.Start(new ProcessStartInfo(url)
        {
            UseShellExecute = true
        });
    }

    private static bool IsDifferentVersion(string current, string latestTag)
    {
        if (string.IsNullOrWhiteSpace(latestTag) || string.Equals(current, "dev", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var normalizedLatest = latestTag.TrimStart('v', 'V');
        return !string.Equals(current, normalizedLatest, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class UpdateCheckResult
{
    public string CurrentVersion { get; set; } = string.Empty;

    public string LatestTag { get; set; } = string.Empty;

    public string ReleaseUrl { get; set; } = string.Empty;

    public bool IsUpdateAvailable { get; set; }
}
