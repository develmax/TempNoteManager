using System.IO;
using System.Text;
using System.Text.Json;
using TempNoteManager.Models;

namespace TempNoteManager.Services;

public sealed class OperationJournalStore
{
    private const int MaxEntries = 250;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _directory;
    private readonly string _journalPath;
    private readonly string _sessionBackupsPath;
    private readonly List<OperationJournalEntry> _entries;

    public OperationJournalStore()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _directory = Path.Combine(appData, "TempNoteManager");
        _journalPath = Path.Combine(_directory, "operation-journal.json");
        _sessionBackupsPath = Path.Combine(_directory, "session-backups");
        _entries = Load();
    }

    public string CreateSessionSnapshot(string sessionPath, string reason)
    {
        if (string.IsNullOrWhiteSpace(sessionPath) || !File.Exists(sessionPath))
        {
            return string.Empty;
        }

        Directory.CreateDirectory(_sessionBackupsPath);

        var safeReason = MakeSafeName(reason);
        var fileName = $"{Path.GetFileName(sessionPath)}.{DateTime.Now:yyyyMMdd-HHmmss-fff}.{safeReason}.bak";
        var targetPath = Path.Combine(_sessionBackupsPath, fileName);
        File.Copy(sessionPath, targetPath, overwrite: false);
        return targetPath;
    }

    public void Add(OperationJournalEntry entry)
    {
        _entries.Insert(0, entry);

        if (_entries.Count > MaxEntries)
        {
            _entries.RemoveRange(MaxEntries, _entries.Count - MaxEntries);
        }

        Save();
    }

    public OperationJournalEntry? GetLastUndoable()
    {
        return _entries.FirstOrDefault(entry => entry.CanUndo);
    }

    public IReadOnlyList<OperationJournalEntry> GetRecent(int count = 12)
    {
        return _entries.Take(count).ToList();
    }

    public async Task<OperationUndoResult> UndoAsync(OperationJournalEntry entry, CancellationToken cancellationToken = default)
    {
        var result = new OperationUndoResult();

        foreach (var file in entry.Files.AsEnumerable().Reverse())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!file.CanMoveBack)
            {
                if (file.Action == OperationFileActions.DeleteToRecycleBin)
                {
                    result.Warnings.Add($"{file.DisplayName}: файл был отправлен в системную корзину Windows, восстановите его оттуда вручную.");
                }

                continue;
            }

            if (!File.Exists(file.TargetPath))
            {
                result.Warnings.Add($"{file.DisplayName}: не найден файл для возврата: {file.TargetPath}");
                continue;
            }

            if (File.Exists(file.SourcePath))
            {
                result.Warnings.Add($"{file.DisplayName}: исходный путь уже занят: {file.SourcePath}");
                continue;
            }

            var sourceDirectory = Path.GetDirectoryName(file.SourcePath);
            if (!string.IsNullOrWhiteSpace(sourceDirectory))
            {
                Directory.CreateDirectory(sourceDirectory);
            }

            await MoveFileAsync(file.TargetPath, file.SourcePath, cancellationToken);
            result.RestoredFiles++;
        }

        if (!string.IsNullOrWhiteSpace(entry.SessionBackupPath) && File.Exists(entry.SessionBackupPath))
        {
            var sessionDirectory = Path.GetDirectoryName(entry.SessionPath);
            if (!string.IsNullOrWhiteSpace(sessionDirectory))
            {
                Directory.CreateDirectory(sessionDirectory);
            }

            File.Copy(entry.SessionBackupPath, entry.SessionPath, overwrite: true);
            result.RestoredSession = true;
        }
        else if (!string.IsNullOrWhiteSpace(entry.SessionBackupPath))
        {
            result.Warnings.Add($"Снимок session.xml не найден: {entry.SessionBackupPath}");
        }

        entry.IsUndone = true;
        entry.UndoneAt = DateTimeOffset.UtcNow;
        entry.UndoNote = string.Join(Environment.NewLine, result.Warnings);
        Save();

        return result;
    }

    public string BuildReport(IEnumerable<NoteFileItem> items)
    {
        var itemList = items.ToList();
        var builder = new StringBuilder();
        builder.AppendLine($"Файлов всего: {itemList.Count}");
        builder.AppendLine($"Временных: {itemList.Count(item => item.IsTemporary)}");
        builder.AppendLine($"В категориях: {itemList.Count(item => item.IsCategorized)}");
        builder.AppendLine($"Дубликатов: {itemList.Count(item => item.IsDuplicate)}");
        builder.AppendLine($"Избранных: {itemList.Count(item => item.IsFavorite)}");
        builder.AppendLine($"Заблокированных: {itemList.Count(item => item.IsLocked)}");
        builder.AppendLine();
        builder.AppendLine("Последние операции:");

        foreach (var entry in GetRecent())
        {
            var undone = entry.IsUndone ? " (отменено)" : string.Empty;
            builder.AppendLine($"- {entry.CreatedAt.LocalDateTime:yyyy-MM-dd HH:mm}: {entry.Description}{undone}");
        }

        return builder.ToString();
    }

    private List<OperationJournalEntry> Load()
    {
        try
        {
            if (!File.Exists(_journalPath))
            {
                return new List<OperationJournalEntry>();
            }

            var json = File.ReadAllText(_journalPath);
            return JsonSerializer.Deserialize<List<OperationJournalEntry>>(json, JsonOptions) ?? new List<OperationJournalEntry>();
        }
        catch
        {
            return new List<OperationJournalEntry>();
        }
    }

    private void Save()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(_journalPath, JsonSerializer.Serialize(_entries, JsonOptions));
    }

    private static async Task MoveFileAsync(string sourcePath, string targetPath, CancellationToken cancellationToken)
    {
        try
        {
            File.Move(sourcePath, targetPath);
        }
        catch (IOException)
        {
            await using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            await using var target = new FileStream(targetPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            await source.CopyToAsync(target, cancellationToken);
            File.Delete(sourcePath);
        }
    }

    private static string MakeSafeName(string source)
    {
        var name = string.IsNullOrWhiteSpace(source) ? "operation" : source.Trim();

        foreach (var invalidChar in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalidChar, '_');
        }

        return name;
    }
}
