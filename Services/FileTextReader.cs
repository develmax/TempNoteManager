using System.IO;
using System.Text;

namespace TempNoteManager.Services;

public static class FileTextReader
{
    public static async Task<string> ReadPreviewAsync(string path, int maxCharacters = 2800, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return string.Empty;
        }

        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var buffer = new char[Math.Max(1, maxCharacters)];
        var count = await reader.ReadBlockAsync(buffer, cancellationToken);
        var text = new string(buffer, 0, count);

        if (count == buffer.Length)
        {
            text += Environment.NewLine + "...";
        }

        return NormalizePreview(text);
    }

    public static async Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return string.Empty;
        }

        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return await reader.ReadToEndAsync(cancellationToken);
    }

    private static string NormalizePreview(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "(пусто)";
        }

        return text.Replace("\0", string.Empty).Trim();
    }
}
