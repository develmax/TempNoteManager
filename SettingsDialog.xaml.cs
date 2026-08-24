using System.IO;
using System.Windows;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using TempNoteManager.Models;
using Forms = System.Windows.Forms;

namespace TempNoteManager;

public partial class SettingsDialog : Window
{
    public SettingsDialog(AppSettings settings)
    {
        InitializeComponent();
        Settings = Clone(settings);
        ApplySettingsToUi();
    }

    public AppSettings Settings { get; private set; }

    private void BrowseSession_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Notepad++ session.xml",
            Filter = "session.xml|session.xml|XML|*.xml|Все файлы|*.*",
            FileName = Path.GetFileName(SessionPathBox.Text),
            InitialDirectory = GetExistingDirectory(Path.GetDirectoryName(SessionPathBox.Text))
        };

        if (dialog.ShowDialog(this) == true)
        {
            SessionPathBox.Text = dialog.FileName;
        }
    }

    private void BrowseTrash_Click(object sender, RoutedEventArgs e)
    {
        var selected = BrowseFolder("Папка пользовательской корзины", TrashFolderBox.Text);
        if (!string.IsNullOrWhiteSpace(selected))
        {
            TrashFolderBox.Text = selected;
            TrashModeBox.SelectedValue = TrashModes.CustomFolder;
        }
    }

    private void BrowseCategoryRoot_Click(object sender, RoutedEventArgs e)
    {
        var selected = BrowseFolder("Корневая папка категорий", CategoryRootBox.Text);
        if (!string.IsNullOrWhiteSpace(selected))
        {
            CategoryRootBox.Text = selected;
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        ReadSettingsFromUi();
        DialogResult = true;
    }

    private void ApplySettingsToUi()
    {
        SessionPathBox.Text = Settings.SessionPath;
        AiEnabledBox.IsChecked = Settings.AiEnabled;
        AiAutoSummaryBox.IsChecked = Settings.AiAutoSummary;
        AiEndpointBox.Text = Settings.AiEndpoint;
        AiModelBox.Text = Settings.AiModel;
        AiKeyBox.Password = Settings.AiApiKey;
        TrashModeBox.SelectedValue = Settings.TrashMode;
        TrashFolderBox.Text = Settings.TrashFolderPath;
        CategoryRootBox.Text = Settings.CategoryRootPath;
    }

    private void ReadSettingsFromUi()
    {
        Settings.SessionPath = SessionPathBox.Text.Trim();
        Settings.AiEnabled = AiEnabledBox.IsChecked == true;
        Settings.AiAutoSummary = AiAutoSummaryBox.IsChecked == true;
        Settings.AiEndpoint = AiEndpointBox.Text.Trim();
        Settings.AiModel = AiModelBox.Text.Trim();
        Settings.AiApiKey = AiKeyBox.Password.Trim();
        Settings.TrashMode = TrashModeBox.SelectedValue?.ToString() ?? TrashModes.RecycleBin;
        Settings.TrashFolderPath = TrashFolderBox.Text.Trim();
        Settings.CategoryRootPath = CategoryRootBox.Text.Trim();
        EnsureDefaults(Settings);
    }

    private static AppSettings Clone(AppSettings settings)
    {
        var clone = new AppSettings
        {
            SessionPath = settings.SessionPath,
            AiEnabled = settings.AiEnabled,
            AiAutoSummary = settings.AiAutoSummary,
            AiEndpoint = settings.AiEndpoint,
            AiModel = settings.AiModel,
            AiApiKey = settings.AiApiKey,
            LastSaveFolder = settings.LastSaveFolder,
            TrashMode = settings.TrashMode,
            TrashFolderPath = settings.TrashFolderPath,
            CategoryRootPath = settings.CategoryRootPath,
            Categories = settings.Categories
                .Select(category => new StorageCategory
                {
                    Id = category.Id,
                    Name = category.Name,
                    Description = category.Description,
                    DirectoryPath = category.DirectoryPath,
                    Color = category.Color
                })
                .ToList()
        };

        EnsureDefaults(clone);
        return clone;
    }

    private static void EnsureDefaults(AppSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.SessionPath))
        {
            settings.SessionPath = Services.NotepadPlusPlusSessionService.GetDefaultSessionPath();
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
    }

    private static string? BrowseFolder(string title, string? selectedPath)
    {
        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = title,
            UseDescriptionForTitle = true,
            SelectedPath = GetExistingDirectory(selectedPath)
                           ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };

        return dialog.ShowDialog() == Forms.DialogResult.OK ? dialog.SelectedPath : null;
    }

    private static string? GetExistingDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        return Directory.Exists(path) ? path : null;
    }
}