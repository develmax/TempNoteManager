using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace TempNoteManager.Services;

public static class FileFingerprintService
{
    public static async Task<string> ComputeAsync(string path, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return string.Empty;
        }

        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash);
    }

    public static string ComputeCategoryHash(string name, string description)
    {
        var normalized = $"{name.Trim()}\n{description.Trim()}".ToUpperInvariant();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(hash);
    }
}
