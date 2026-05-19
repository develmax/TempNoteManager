namespace TempNoteManager.Models;

public sealed class TrashOptions
{
    public string Mode { get; set; } = TrashModes.RecycleBin;

    public string CustomFolderPath { get; set; } = string.Empty;

    public bool UseCustomFolder => Mode == TrashModes.CustomFolder;
}

public static class TrashModes
{
    public const string RecycleBin = "RecycleBin";
    public const string CustomFolder = "CustomFolder";
}
