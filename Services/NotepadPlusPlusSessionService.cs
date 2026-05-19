using System.IO;
using System.Xml.Linq;
using Microsoft.VisualBasic.FileIO;
using TempNoteManager.Models;

namespace TempNoteManager.Services;

public sealed class NotepadPlusPlusSessionService
{
    public static string GetDefaultSessionPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "Notepad++", "session.xml");
    }

    public async Task<IReadOnlyList<NoteFileItem>> LoadAsync(string sessionPath, CancellationToken cancellationToken = default)
    {
        var items = new List<NoteFileItem>();
        var backupDirectory = GetBackupDirectory(sessionPath);

        if (File.Exists(sessionPath))
        {
            var document = XDocument.Load(sessionPath, LoadOptions.PreserveWhitespace);
            foreach (var fileElement in document.Descendants("File"))
            {
                cancellationToken.ThrowIfCancellationRequested();
                items.Add(await CreateItemFromSessionElementAsync(fileElement, sessionPath, backupDirectory, cancellationToken));
            }
        }
        else if (Directory.Exists(backupDirectory))
        {
            foreach (var backupFile in Directory.GetFiles(backupDirectory).OrderByDescending(File.GetLastWriteTime))
            {
                cancellationToken.ThrowIfCancellationRequested();
                items.Add(await CreateItemFromBackupFileAsync(backupFile, sessionPath, cancellationToken));
            }
        }

        return items;
    }

    public async Task SaveTemporaryAsPermanentAsync(NoteFileItem item, string targetPath, CancellationToken cancellationToken = default)
    {
        if (!item.IsTemporary)
        {
            throw new InvalidOperationException("Выбранный файл уже постоянный.");
        }

        if (!File.Exists(item.ContentPath))
        {
            throw new FileNotFoundException("Не найден временный файл.", item.ContentPath);
        }

        var directory = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using (var source = new FileStream(item.ContentPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        await using (var target = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.Read))
        {
            await source.CopyToAsync(target, cancellationToken);
        }

        if (File.Exists(item.SessionPath))
        {
            var document = LoadSessionForWrite(item.SessionPath);
            var element = FindFileElement(document, item)
                          ?? throw new InvalidOperationException("Запись файла не найдена в session.xml.");

            SetElementAsPermanent(element, targetPath);
            SaveSession(document, item.SessionPath);
        }
    }

    public async Task DeleteItemAsync(NoteFileItem item, TrashOptions options, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(item.ContentPath))
        {
            RemoveFromSession(item);
            return;
        }

        if (options.UseCustomFolder)
        {
            if (string.IsNullOrWhiteSpace(options.CustomFolderPath))
            {
                throw new InvalidOperationException("Не выбрана папка кастомной корзины.");
            }

            Directory.CreateDirectory(options.CustomFolderPath);
            var targetPath = GetUniquePath(options.CustomFolderPath, BuildSafeFileName(item.DisplayName, item.ContentPath));
            await MoveFileAsync(item.ContentPath, targetPath, cancellationToken);
        }
        else
        {
            FileSystem.DeleteFile(
                item.ContentPath,
                UIOption.OnlyErrorDialogs,
                RecycleOption.SendToRecycleBin);
        }

        RemoveFromSession(item);
    }

    public async Task<string> MoveItemToDirectoryAsync(NoteFileItem item, string targetDirectory, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(item.ContentPath))
        {
            throw new FileNotFoundException("Не найден файл для перемещения.", item.ContentPath);
        }

        if (string.IsNullOrWhiteSpace(targetDirectory))
        {
            throw new InvalidOperationException("Не выбрана целевая директория.");
        }

        Directory.CreateDirectory(targetDirectory);
        var targetPath = GetUniquePath(targetDirectory, BuildSafeFileName(item.DisplayName, item.ContentPath));

        if (PathsEqual(item.ContentPath, targetPath))
        {
            return targetPath;
        }

        await MoveFileAsync(item.ContentPath, targetPath, cancellationToken);

        if (File.Exists(item.SessionPath))
        {
            var document = LoadSessionForWrite(item.SessionPath);
            var element = FindFileElement(document, item)
                          ?? throw new InvalidOperationException("Запись файла не найдена в session.xml.");

            SetElementAsPermanent(element, targetPath);
            SaveSession(document, item.SessionPath);
        }

        return targetPath;
    }

    public async Task<string> RenameItemAsync(NoteFileItem item, string newFileName, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(item.ContentPath))
        {
            throw new FileNotFoundException("Не найден файл для переименования.", item.ContentPath);
        }

        var targetPath = BuildRenameTargetPath(item.ContentPath, newFileName);
        if (PathsEqual(item.ContentPath, targetPath))
        {
            return item.ContentPath;
        }

        if (File.Exists(targetPath))
        {
            throw new IOException($"Файл уже существует: {targetPath}");
        }

        await MoveFileAsync(item.ContentPath, targetPath, cancellationToken);

        if (File.Exists(item.SessionPath))
        {
            var document = LoadSessionForWrite(item.SessionPath);
            var element = FindFileElement(document, item)
                          ?? throw new InvalidOperationException("Запись файла не найдена в session.xml.");

            if (item.IsTemporary)
            {
                SetElementAsTemporary(element, Path.GetFileName(targetPath), targetPath);
            }
            else
            {
                SetElementAsPermanent(element, targetPath);
            }

            SaveSession(document, item.SessionPath);
        }

        return targetPath;
    }

    public async Task ConvertPermanentToTemporaryAsync(NoteFileItem item, CancellationToken cancellationToken = default)
    {
        if (item.IsTemporary)
        {
            throw new InvalidOperationException("Выбранный файл уже временный.");
        }

        if (!File.Exists(item.ContentPath))
        {
            throw new FileNotFoundException("Не найден исходный файл.", item.ContentPath);
        }

        if (!File.Exists(item.SessionPath))
        {
            throw new FileNotFoundException("Не найден session.xml.", item.SessionPath);
        }

        var document = LoadSessionForWrite(item.SessionPath);
        var element = FindFileElement(document, item)
                      ?? throw new InvalidOperationException("Запись файла не найдена в session.xml.");

        var backupDirectory = GetBackupDirectory(item.SessionPath);
        Directory.CreateDirectory(backupDirectory);

        var label = GetNextTemporaryLabel(document);
        var backupPath = Path.Combine(backupDirectory, $"{label}@{DateTime.Now:yyyyMMdd-HHmmss}");

        await using (var source = new FileStream(item.ContentPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        await using (var target = new FileStream(backupPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
        {
            await source.CopyToAsync(target, cancellationToken);
        }

        element.SetAttributeValue("filename", label);
        element.SetAttributeValue("backupFilePath", backupPath);
        element.SetAttributeValue("originalFileLastModifTimestamp", "0");
        element.SetAttributeValue("originalFileLastModifTimestampHigh", "0");
        SaveSession(document, item.SessionPath);
    }

    public void SaveOrder(string sessionPath, IEnumerable<NoteFileItem> orderedItems)
    {
        if (!File.Exists(sessionPath))
        {
            throw new FileNotFoundException("Не найден session.xml.", sessionPath);
        }

        var document = LoadSessionForWrite(sessionPath);
        var session = document.Descendants("Session").FirstOrDefault()
                      ?? throw new InvalidOperationException("В session.xml не найден узел Session.");

        var fileElements = session.Elements("File").ToList();
        if (fileElements.Count == 0)
        {
            return;
        }

        var ordered = new List<XElement>();
        var used = new HashSet<XElement>();

        foreach (var item in orderedItems)
        {
            var match = fileElements.FirstOrDefault(element => !used.Contains(element) && GetElementKey(element) == item.SessionKey);
            if (match is not null)
            {
                ordered.Add(match);
                used.Add(match);
            }
        }

        ordered.AddRange(fileElements.Where(element => !used.Contains(element)));

        foreach (var element in fileElements)
        {
            element.Remove();
        }

        session.Add(ordered);
        SaveSession(document, sessionPath);
    }

    private static void RemoveFromSession(NoteFileItem item)
    {
        if (!File.Exists(item.SessionPath))
        {
            return;
        }

        var document = LoadSessionForWrite(item.SessionPath);
        var element = FindFileElement(document, item);
        if (element is null)
        {
            return;
        }

        element.Remove();
        SaveSession(document, item.SessionPath);
    }

    private static async Task<NoteFileItem> CreateItemFromSessionElementAsync(
        XElement fileElement,
        string sessionPath,
        string backupDirectory,
        CancellationToken cancellationToken)
    {
        var fileName = ReadAttribute(fileElement, "filename");
        var backupPath = ReadAttribute(fileElement, "backupFilePath");
        var originalPath = IsRootedPath(fileName) ? fileName : string.Empty;
        var contentPath = ResolveContentPath(fileName, backupPath, backupDirectory, out var isTemporary, out var isInTemporaryStorage);
        var displayName = ResolveDisplayName(fileName, contentPath, isTemporary);
        var item = new NoteFileItem
        {
            SessionKey = BuildSessionKey(fileName, backupPath),
            SessionPath = sessionPath,
            FileName = fileName,
            BackupPath = backupPath,
            OriginalPath = originalPath,
            ContentPath = contentPath,
            DisplayName = displayName,
            IsTemporary = isTemporary,
            IsInTemporaryStorage = isInTemporaryStorage,
            Language = ReadAttribute(fileElement, "lang"),
            EncodingHint = ReadAttribute(fileElement, "encoding")
        };

        await FillMetadataAndPreviewAsync(item, cancellationToken);
        return item;
    }

    private static async Task<NoteFileItem> CreateItemFromBackupFileAsync(
        string backupFile,
        string sessionPath,
        CancellationToken cancellationToken)
    {
        var displayName = Path.GetFileName(backupFile);
        var item = new NoteFileItem
        {
            SessionKey = "backup:" + NormalizeKeyPart(backupFile),
            SessionPath = sessionPath,
            FileName = displayName,
            BackupPath = backupFile,
            ContentPath = backupFile,
            DisplayName = displayName,
            IsTemporary = true,
            IsInTemporaryStorage = true
        };

        await FillMetadataAndPreviewAsync(item, cancellationToken);
        return item;
    }

    private static async Task FillMetadataAndPreviewAsync(NoteFileItem item, CancellationToken cancellationToken)
    {
        item.Exists = File.Exists(item.ContentPath);

        if (item.Exists)
        {
            var info = new FileInfo(item.ContentPath);
            item.CreatedAt = info.CreationTime;
            item.ModifiedAt = info.LastWriteTime;
            item.SizeBytes = info.Length;
            item.ContentFingerprint = await FileFingerprintService.ComputeAsync(item.ContentPath, cancellationToken);
            item.PreviewText = await FileTextReader.ReadPreviewAsync(item.ContentPath, cancellationToken: cancellationToken);
        }
        else
        {
            item.PreviewText = "Файл не найден.";
        }

        item.RefreshDerivedProperties();
    }

    private static string ResolveContentPath(
        string fileName,
        string backupPath,
        string backupDirectory,
        out bool isTemporary,
        out bool isInTemporaryStorage)
    {
        var fileExists = IsRootedPath(fileName) && File.Exists(fileName);
        var backupExists = IsRootedPath(backupPath) && File.Exists(backupPath);
        var looksTemporary = !IsRootedPath(fileName) || fileName.StartsWith("new ", StringComparison.OrdinalIgnoreCase);

        isTemporary = looksTemporary && backupExists;

        var shouldUseBackup = backupExists && (isTemporary || !fileExists || IsBackupNewer(fileName, backupPath));
        var contentPath = shouldUseBackup
            ? backupPath
            : fileExists
                ? fileName
                : backupExists
                    ? backupPath
                    : fileName;

        isInTemporaryStorage = shouldUseBackup && IsUnderDirectory(backupPath, backupDirectory);
        return contentPath;
    }

    private static bool IsBackupNewer(string fileName, string backupPath)
    {
        try
        {
            return File.GetLastWriteTimeUtc(backupPath) > File.GetLastWriteTimeUtc(fileName);
        }
        catch
        {
            return false;
        }
    }

    private static string ResolveDisplayName(string fileName, string contentPath, bool isTemporary)
    {
        if (isTemporary && !string.IsNullOrWhiteSpace(fileName))
        {
            return fileName;
        }

        var candidate = !string.IsNullOrWhiteSpace(fileName) ? fileName : contentPath;

        try
        {
            var name = Path.GetFileName(candidate);
            return string.IsNullOrWhiteSpace(name) ? candidate : name;
        }
        catch
        {
            return candidate;
        }
    }

    private static string GetBackupDirectory(string sessionPath)
    {
        var sessionDirectory = Path.GetDirectoryName(sessionPath);
        if (string.IsNullOrWhiteSpace(sessionDirectory))
        {
            sessionDirectory = Path.GetDirectoryName(GetDefaultSessionPath());
        }

        return Path.Combine(sessionDirectory ?? string.Empty, "backup");
    }

    private static XDocument LoadSessionForWrite(string sessionPath)
    {
        BackupSession(sessionPath);
        return XDocument.Load(sessionPath, LoadOptions.PreserveWhitespace);
    }

    private static void SaveSession(XDocument document, string sessionPath)
    {
        document.Save(sessionPath);
    }

    private static void BackupSession(string sessionPath)
    {
        if (!File.Exists(sessionPath))
        {
            return;
        }

        var backupPath = $"{sessionPath}.{DateTime.Now:yyyyMMdd-HHmmss}.bak";
        if (File.Exists(backupPath))
        {
            backupPath = $"{sessionPath}.{DateTime.Now:yyyyMMdd-HHmmss-fff}.bak";
        }

        File.Copy(sessionPath, backupPath, overwrite: false);
    }

    private static XElement? FindFileElement(XDocument document, NoteFileItem item)
    {
        return document
            .Descendants("File")
            .FirstOrDefault(element => GetElementKey(element) == item.SessionKey);
    }

    private static string GetElementKey(XElement element)
    {
        return BuildSessionKey(ReadAttribute(element, "filename"), ReadAttribute(element, "backupFilePath"));
    }

    private static string BuildSessionKey(string fileName, string backupPath)
    {
        return $"{NormalizeKeyPart(fileName)}|{NormalizeKeyPart(backupPath)}";
    }

    private static string NormalizeKeyPart(string value)
    {
        return value.Trim().Replace('/', '\\').ToUpperInvariant();
    }

    private static string ReadAttribute(XElement element, string name)
    {
        return element.Attribute(name)?.Value ?? string.Empty;
    }

    private static bool IsRootedPath(string path)
    {
        try
        {
            return !string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsUnderDirectory(string path, string directory)
    {
        try
        {
            var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var fullDirectory = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return fullPath.StartsWith(fullDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                   || fullPath.Equals(fullDirectory, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static void SetOriginalTimestamp(XElement element, string targetPath)
    {
        var fileTime = File.GetLastWriteTimeUtc(targetPath).ToFileTimeUtc();
        var low = unchecked((uint)(fileTime & 0xffffffff));
        var high = unchecked((uint)((fileTime >> 32) & 0xffffffff));

        element.SetAttributeValue("originalFileLastModifTimestamp", low.ToString());
        element.SetAttributeValue("originalFileLastModifTimestampHigh", high.ToString());
    }

    private static void SetElementAsPermanent(XElement element, string targetPath)
    {
        element.SetAttributeValue("filename", targetPath);
        element.SetAttributeValue("backupFilePath", string.Empty);
        SetOriginalTimestamp(element, targetPath);
    }

    private static void SetElementAsTemporary(XElement element, string fileName, string backupPath)
    {
        element.SetAttributeValue("filename", fileName);
        element.SetAttributeValue("backupFilePath", backupPath);
        element.SetAttributeValue("originalFileLastModifTimestamp", "0");
        element.SetAttributeValue("originalFileLastModifTimestampHigh", "0");
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

    private static string GetUniquePath(string directory, string fileName)
    {
        var baseName = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        var path = Path.Combine(directory, fileName);
        var index = 2;

        while (File.Exists(path))
        {
            path = Path.Combine(directory, $"{baseName} ({index}){extension}");
            index++;
        }

        return path;
    }

    private static string BuildSafeFileName(string displayName, string sourcePath)
    {
        var fallback = Path.GetFileName(sourcePath);
        var name = string.IsNullOrWhiteSpace(displayName) ? fallback : displayName.Trim();

        foreach (var invalidChar in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalidChar, '_');
        }

        if (string.IsNullOrWhiteSpace(Path.GetExtension(name)))
        {
            var sourceExtension = Path.GetExtension(sourcePath);
            name += string.IsNullOrWhiteSpace(sourceExtension) ? ".txt" : sourceExtension;
        }

        return string.IsNullOrWhiteSpace(name) ? "note.txt" : name;
    }

    private static string BuildRenameTargetPath(string sourcePath, string newFileName)
    {
        var directory = Path.GetDirectoryName(sourcePath)
                        ?? throw new InvalidOperationException("Не удалось определить директорию файла.");
        var name = newFileName.Trim();

        foreach (var invalidChar in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalidChar, '_');
        }

        if (string.IsNullOrWhiteSpace(Path.GetExtension(name)))
        {
            name += Path.GetExtension(sourcePath);
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException("Пустое имя файла.");
        }

        return Path.Combine(directory, name);
    }

    private static bool PathsEqual(string left, string right)
    {
        try
        {
            return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static string GetNextTemporaryLabel(XDocument document)
    {
        var max = 0;
        foreach (var fileName in document.Descendants("File").Select(element => ReadAttribute(element, "filename")))
        {
            if (!fileName.StartsWith("new ", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (int.TryParse(fileName[4..].Trim(), out var number))
            {
                max = Math.Max(max, number);
            }
        }

        return $"new {max + 1}";
    }
}
