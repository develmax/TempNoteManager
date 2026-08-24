namespace TempNoteManager.Models;

public sealed class OperationJournalEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public string Kind { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string SessionPath { get; set; } = string.Empty;

    public string SessionBackupPath { get; set; } = string.Empty;

    public List<OperationFileChange> Files { get; set; } = new();

    public bool IsUndone { get; set; }

    public DateTimeOffset? UndoneAt { get; set; }

    public string UndoNote { get; set; } = string.Empty;

    public bool CanUndo => !IsUndone
                           && (!string.IsNullOrWhiteSpace(SessionBackupPath)
                               || Files.Any(file => file.CanMoveBack));
}

public sealed class OperationFileChange
{
    public string Action { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string SourcePath { get; set; } = string.Empty;

    public string TargetPath { get; set; } = string.Empty;

    public bool CanMoveBack => Action is not OperationFileActions.CreateCopy
                               and not OperationFileActions.SessionOnly
                               and not OperationFileActions.DeleteToRecycleBin
                               && !string.IsNullOrWhiteSpace(SourcePath)
                               && !string.IsNullOrWhiteSpace(TargetPath);
}

public static class OperationKinds
{
    public const string Delete = "Delete";
    public const string Move = "Move";
    public const string Rename = "Rename";
    public const string SaveAsPermanent = "SaveAsPermanent";
    public const string ConvertToTemporary = "ConvertToTemporary";
    public const string SaveOrder = "SaveOrder";
    public const string ExportTemporary = "ExportTemporary";
}

public static class OperationFileActions
{
    public const string Move = "Move";
    public const string Rename = "Rename";
    public const string DeleteToCustomTrash = "DeleteToCustomTrash";
    public const string DeleteToRecycleBin = "DeleteToRecycleBin";
    public const string CreateCopy = "CreateCopy";
    public const string SessionOnly = "SessionOnly";
}

public sealed class OperationUndoResult
{
    public bool RestoredSession { get; set; }

    public int RestoredFiles { get; set; }

    public List<string> Warnings { get; } = new();
}
