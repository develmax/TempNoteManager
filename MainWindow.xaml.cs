using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using TempNoteManager.Models;
using TempNoteManager.Services;
using Forms = System.Windows.Forms;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;
using WpfDataGrid = System.Windows.Controls.DataGrid;
using WpfDragEventArgs = System.Windows.DragEventArgs;
using WpfListBox = System.Windows.Controls.ListBox;
using WpfMessageBox = System.Windows.MessageBox;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;
using WpfPoint = System.Windows.Point;
using WpfDragDropEffects = System.Windows.DragDropEffects;

namespace TempNoteManager;

public partial class MainWindow : Window
{
    private const string DragSourceFiles = "Files";
    private const string DragSourceCategory = "Category";

    private readonly AppSettingsStore _settingsStore = new();
    private readonly NotepadPlusPlusSessionService _sessionService = new();
    private readonly AiSummaryService _aiSummaryService = new();
    private readonly AiAnalysisCacheStore _aiCacheStore = new();
    private readonly FileStateStore _fileStateStore = new();
    private readonly OperationJournalStore _journalStore = new();
    private readonly UpdateCheckService _updateCheckService = new();
    private readonly Dictionary<string, Task> _summaryTasks = new();

    private AppSettings _settings = new();
    private CancellationTokenSource? _refreshCancellation;
    private WpfPoint _dragStartPoint;
    private NoteFileItem? _draggedItem;
    private NoteFileItem? _selectedItem;
    private string _dragSourceKind = DragSourceFiles;
    private bool _syncingSelection;

    public MainWindow()
    {
        InitializeComponent();

        ItemsView = CollectionViewSource.GetDefaultView(Items);
        ItemsView.Filter = FilterItem;
        DataContext = this;
    }

    public ObservableCollection<NoteFileItem> Items { get; } = new();

    public ObservableCollection<StorageCategory> Categories { get; } = new();

    public ICollectionView ItemsView { get; }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _settings = _settingsStore.Load();
        EnsureSettingsDefaults();
        LoadCategoriesFromSettings();
        ApplyViewMode();
        await RefreshAsync();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        await RefreshAsync();
    }

    private async void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (IsTextInputFocused())
        {
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.R)
        {
            e.Handled = true;
            await RefreshAsync();
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Z)
        {
            e.Handled = true;
            await UndoLastOperationAsync();
            return;
        }

        if (e.Key == Key.Delete)
        {
            e.Handled = true;
            await DeleteItemsAsync(GetSelectedItems());
            return;
        }

        if (e.Key == Key.F2)
        {
            e.Handled = true;
            var item = GetSelectedItem();
            if (item is not null)
            {
                await RenameItemAsync(item);
            }
        }
    }

    private static bool IsTextInputFocused()
    {
        return Keyboard.FocusedElement is System.Windows.Controls.TextBox or PasswordBox or System.Windows.Controls.ComboBox;
    }

    private async void Settings_Click(object sender, RoutedEventArgs e)
    {
        await OpenSettingsDialogAsync();
    }

    private async Task OpenSettingsDialogAsync()
    {
        try
        {
            PersistCategoriesToSettings();
            var dialog = new SettingsDialog(_settings)
            {
                Owner = this
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            _settings = dialog.Settings;
            EnsureSettingsDefaults();
            SaveSettingsToStore();
            LoadCategoriesFromSettings();
            UpdateCategoryMembership();
            ApplyAiCacheToItems();
            ApplyFileStateToItems();
            ComputeDuplicates();
            ItemsView.Refresh();
            SetStatus("Настройки сохранены.");
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            ShowOperationError("Не удалось сохранить настройки.", ex);
        }
    }

    private void SaveSettingsToStore()
    {
        PersistCategoriesToSettings();
        _settingsStore.Save(_settings);

        if (string.IsNullOrWhiteSpace(_settings.AiApiKey))
        {
            _settingsStore.DeleteAiApiKey();
        }
        else
        {
            _settingsStore.SaveAiApiKey(_settings.AiApiKey);
        }
    }

    private void CreateCategory_Click(object sender, RoutedEventArgs e)
    {
        ReadSettingsFromUi();
        Directory.CreateDirectory(_settings.CategoryRootPath);

        var dialog = new CategoryDialog
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var directoryPath = GetUniqueDirectoryPath(_settings.CategoryRootPath, dialog.CategoryName);
        Directory.CreateDirectory(directoryPath);

        var category = new StorageCategory
        {
            Name = dialog.CategoryName,
            Description = dialog.Description,
            Color = dialog.Color,
            DirectoryPath = directoryPath
        };

        Categories.Add(category);
        PersistCategoriesToSettings();
        _settingsStore.Save(_settings);
        UpdateCategoryMembership();
        ApplyAiCacheToItems();
        SetStatus($"Категория создана: {category.Name}");
    }

    private void EditCategory_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not StorageCategory category)
        {
            return;
        }

        var dialog = new CategoryDialog(category)
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        category.Name = dialog.CategoryName;
        category.Description = dialog.Description;
        category.Color = dialog.Color;

        PersistCategoriesToSettings();
        _settingsStore.Save(_settings);
        UpdateCategoryMembership();
        ApplyAiCacheToItems();
        ApplyFileStateToItems();
        ComputeDuplicates();
        ItemsView.Refresh();
        SetStatus($"Категория обновлена: {category.Name}");
    }

    private async void UndoLast_Click(object sender, RoutedEventArgs e)
    {
        await UndoLastOperationAsync();
    }

    private async void DeleteSelected_Click(object sender, RoutedEventArgs e)
    {
        var items = GetSelectedItems();
        if (items.Count == 0)
        {
            SetStatus("Выберите файл для удаления.");
            return;
        }

        await DeleteItemsAsync(items);
    }

    private async void RenameSelected_Click(object sender, RoutedEventArgs e)
    {
        var item = GetSelectedItem();
        if (item is null)
        {
            SetStatus("Выберите файл для переименования.");
            return;
        }

        await RenameItemAsync(item);
    }

    private async void MoveSelectedToCategory_Click(object sender, RoutedEventArgs e)
    {
        var items = GetSelectedItems().Where(item => item.Exists).ToList();
        if (items.Count == 0)
        {
            SetStatus("Выберите файлы для переноса в категорию.");
            return;
        }

        if (Categories.Count == 0)
        {
            SetStatus("Сначала создайте категорию.");
            return;
        }

        var dialog = new CategoryPickerDialog(Categories)
        {
            Owner = this
        };

        if (dialog.ShowDialog() == true && dialog.SelectedCategory is not null)
        {
            await MoveItemsToCategoryAsync(items, dialog.SelectedCategory);
        }
    }

    private void ToggleLock_Click(object sender, RoutedEventArgs e)
    {
        ToggleStateForSelected(item => item.IsLocked, (item, value) => item.IsLocked = value, "блокировка");
    }

    private void ToggleFavorite_Click(object sender, RoutedEventArgs e)
    {
        ToggleStateForSelected(item => item.IsFavorite, (item, value) => item.IsFavorite = value, "избранное");
    }

    private void ShowReport_Click(object sender, RoutedEventArgs e)
    {
        WpfMessageBox.Show(this, _journalStore.BuildReport(Items), "Отчет TempNoteManager", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async void CheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SetStatus("Проверяю последний релиз на GitHub...");
            var result = await _updateCheckService.CheckLatestAsync();
            var message = result.IsUpdateAvailable
                ? $"Доступен релиз {result.LatestTag}. Открыть страницу релиза?"
                : $"Последний релиз: {result.LatestTag}. Текущая сборка: {result.CurrentVersion}.";

            var choice = WpfMessageBox.Show(
                this,
                message,
                "Обновления",
                result.IsUpdateAvailable ? MessageBoxButton.YesNo : MessageBoxButton.OK,
                MessageBoxImage.Information);

            if (result.IsUpdateAvailable && choice == MessageBoxResult.Yes)
            {
                UpdateCheckService.OpenReleasePage(result.ReleaseUrl);
            }

            SetStatus(result.IsUpdateAvailable ? $"Доступно обновление: {result.LatestTag}" : "Обновлений не найдено.");
        }
        catch (Exception ex)
        {
            ShowOperationError("Не удалось проверить обновления.", ex);
        }
    }

    private async void RenameFile_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is NoteFileItem item)
        {
            await RenameItemAsync(item);
        }
    }

    private void MoveSelectedUp_Click(object sender, RoutedEventArgs e)
    {
        MoveSelectedBy(-1);
    }

    private void MoveSelectedDown_Click(object sender, RoutedEventArgs e)
    {
        MoveSelectedBy(1);
    }

    private async void SaveTemporaryAsPermanent_Click(object sender, RoutedEventArgs e)
    {
        var item = GetSelectedItem();
        if (item is null)
        {
            return;
        }

        if (!item.CanSaveAsPermanent)
        {
            SetStatus("Выбранный файл нельзя сохранить как постоянный.");
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Сохранить временный файл",
            FileName = BuildSafeFileName(item.DisplayName),
            InitialDirectory = GetExistingDirectory(_settings.LastSaveFolder)
                               ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Filter = "Текстовые файлы|*.txt|Все файлы|*.*"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        if (!EnsureItemsUnlocked(new[] { item }, "сохранить как постоянный"))
        {
            return;
        }

        if (!ConfirmSessionOperation(
                "Сохранить как постоянный",
                $"Файл будет скопирован в выбранный путь, а запись в session.xml станет постоянной.\n\nФайл: {item.DisplayName}\nНовый путь: {dialog.FileName}",
                MessageBoxImage.Question))
        {
            return;
        }

        try
        {
            ReadSettingsFromUi();
            var sessionBackup = _journalStore.CreateSessionSnapshot(_settings.SessionPath, OperationKinds.SaveAsPermanent);
            await _sessionService.SaveTemporaryAsPermanentAsync(item, dialog.FileName);
            _journalStore.Add(new OperationJournalEntry
            {
                Kind = OperationKinds.SaveAsPermanent,
                Description = $"Сохранение временного файла как постоянного: {item.DisplayName}",
                SessionPath = _settings.SessionPath,
                SessionBackupPath = sessionBackup,
                Files =
                {
                    new OperationFileChange
                    {
                        Action = OperationFileActions.CreateCopy,
                        DisplayName = item.DisplayName,
                        SourcePath = item.ContentPath,
                        TargetPath = dialog.FileName
                    }
                }
            });
            _settings.LastSaveFolder = Path.GetDirectoryName(dialog.FileName) ?? string.Empty;
            _settingsStore.Save(_settings);
            SetStatus($"Файл стал постоянным для Notepad++: {dialog.FileName}");
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            ShowOperationError("Не удалось сохранить временный файл.", ex);
        }
    }

    private async void ConvertToTemporary_Click(object sender, RoutedEventArgs e)
    {
        var item = GetSelectedItem();
        if (item is null)
        {
            return;
        }

        if (!item.CanConvertToTemporary)
        {
            SetStatus("Выбранный файл уже временный или недоступен.");
            return;
        }

        if (!EnsureItemsUnlocked(new[] { item }, "сделать временным"))
        {
            return;
        }

        if (!ConfirmSessionOperation(
                "Сделать файл временным",
                $"Будет создана временная копия в backup-папке Notepad++, а запись в session.xml станет временной.\n\nФайл: {item.DisplayName}",
                MessageBoxImage.Warning))
        {
            return;
        }

        try
        {
            var sessionBackup = _journalStore.CreateSessionSnapshot(_settings.SessionPath, OperationKinds.ConvertToTemporary);
            await _sessionService.ConvertPermanentToTemporaryAsync(item);
            _journalStore.Add(new OperationJournalEntry
            {
                Kind = OperationKinds.ConvertToTemporary,
                Description = $"Конвертация постоянного файла во временный: {item.DisplayName}",
                SessionPath = _settings.SessionPath,
                SessionBackupPath = sessionBackup,
                Files =
                {
                    new OperationFileChange
                    {
                        Action = OperationFileActions.SessionOnly,
                        DisplayName = item.DisplayName,
                        SourcePath = item.ContentPath
                    }
                }
            });
            SetStatus($"Файл переведен во временный: {item.DisplayName}");
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            ShowOperationError("Не удалось перевести файл во временный.", ex);
        }
    }

    private async void ExportTemporary_Click(object sender, RoutedEventArgs e)
    {
        var temporaryItems = Items.Where(item => item.IsTemporary && item.Exists).ToList();
        if (temporaryItems.Count == 0)
        {
            SetStatus("Нет доступных временных файлов для экспорта.");
            return;
        }

        var selected = BrowseFolder("Папка для временных файлов", _settings.LastSaveFolder);
        if (string.IsNullOrWhiteSpace(selected))
        {
            return;
        }

        try
        {
            foreach (var item in temporaryItems)
            {
                var targetPath = GetUniquePath(selected, BuildSafeFileName(item.DisplayName));
                await using var source = new FileStream(item.ContentPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                await using var target = new FileStream(targetPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
                await source.CopyToAsync(target);
            }

            _settings.LastSaveFolder = selected;
            _settingsStore.Save(_settings);
            SetStatus($"Экспортировано временных файлов: {temporaryItems.Count}.");
        }
        catch (Exception ex)
        {
            ShowOperationError("Не удалось экспортировать временные файлы.", ex);
        }
    }

    private async void Summarize_Click(object sender, RoutedEventArgs e)
    {
        var item = GetSelectedItem();
        if (item is not null)
        {
            await EnsureSummaryAsync(item, force: true);
        }
    }

    private async void AnalyzeVisibleWithAi_Click(object sender, RoutedEventArgs e)
    {
        ReadSettingsFromUi();

        if (!_settings.AiEnabled)
        {
            SetStatus("AI выключен.");
            return;
        }

        if (Categories.Count == 0)
        {
            SetStatus("Сначала создайте категории с описанием для AI.");
            return;
        }

        var selectedItems = GetSelectedItems();
        var targetItems = (selectedItems.Count > 0 ? selectedItems : ItemsView.Cast<NoteFileItem>().ToList())
            .Where(item => item.Exists)
            .Distinct()
            .ToList();

        if (targetItems.Count == 0)
        {
            SetStatus("Нет файлов для AI-разметки.");
            return;
        }

        var plan = targetItems
            .Select(item => new
            {
                Item = item,
                Categories = _aiCacheStore.GetCategoriesNeedingClassification(item, Categories)
            })
            .ToList();
        var requestCount = plan.Count(row => row.Categories.Count > 0);
        var fromCache = plan.Count - requestCount;

        if (requestCount == 0)
        {
            foreach (var item in targetItems)
            {
                _aiCacheStore.ApplyToItem(item, Categories);
            }

            ItemsView.Refresh();
            SetStatus($"AI-разметка уже есть в кэше: {fromCache}.");
            return;
        }

        var approxChars = plan
            .Where(row => row.Categories.Count > 0)
            .Sum(row => row.Item.Exists ? Math.Min(row.Item.SizeBytes, 7000) : 0);
        var approxTokens = Math.Max(1, (int)Math.Ceiling(approxChars / 4.0)) + requestCount * 260;
        var scope = selectedItems.Count > 0 ? "выбранным файлам" : "видимому списку";

        if (!ConfirmAiOperation(
                "AI-разметка",
                $"AI будет запущен по {scope}.\n\nФайлов: {targetItems.Count}\nНовых AI-запросов: {requestCount}\nИз кэша: {fromCache}\nПримерная оценка объема: до {approxTokens:N0} токенов"))
        {
            return;
        }

        var analyzed = 0;
        var cacheHits = 0;
        foreach (var row in plan)
        {
            if (row.Categories.Count == 0)
            {
                _aiCacheStore.ApplyToItem(row.Item, Categories);
                cacheHits++;
                continue;
            }

            analyzed++;
            SetStatus($"AI размечает {analyzed}/{requestCount}: {row.Item.DisplayName}; новых/измененных категорий: {row.Categories.Count}");

            try
            {
                var tags = await _aiSummaryService.ClassifyAsync(row.Item, row.Categories, _settings);
                _aiCacheStore.SaveClassification(row.Item, row.Categories, tags, Categories);
            }
            catch (Exception ex)
            {
                row.Item.SummaryState = ex.Message;
            }
        }

        ItemsView.Refresh();
        SetStatus($"AI-разметка готова: AI-запросов {analyzed}; из кэша {cacheHits}.");
    }
    private async void SuggestCategoriesWithAi_Click(object sender, RoutedEventArgs e)
    {
        ReadSettingsFromUi();

        if (!_settings.AiEnabled)
        {
            SetStatus("AI выключен.");
            return;
        }

        var visibleItems = ItemsView.Cast<NoteFileItem>().Where(item => item.Exists).ToList();
        if (visibleItems.Count == 0)
        {
            SetStatus("Нет видимых файлов для анализа категорий.");
            return;
        }

        try
        {
            SetStatus("AI ищет недостающие категории...");
            var suggestions = await _aiSummaryService.SuggestMissingCategoriesAsync(visibleItems, Categories, _settings);
            if (suggestions.Count == 0)
            {
                SetStatus("AI не предложил новых категорий.");
                return;
            }

            var dialog = new SuggestedCategoriesDialog(suggestions)
            {
                Owner = this
            };

            if (dialog.ShowDialog() != true)
            {
                SetStatus($"AI предложил категорий: {suggestions.Count}.");
                return;
            }

            var created = 0;
            ReadSettingsFromUi();
            Directory.CreateDirectory(_settings.CategoryRootPath);

            foreach (var suggestion in dialog.SelectedSuggestions)
            {
                var safeName = MakeSafeName(suggestion.Name);
                if (string.IsNullOrWhiteSpace(safeName)
                    || Categories.Any(category => category.Name.Equals(safeName, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                var directoryPath = GetUniqueDirectoryPath(_settings.CategoryRootPath, safeName);
                Directory.CreateDirectory(directoryPath);

                Categories.Add(new StorageCategory
                {
                    Name = safeName,
                    Description = suggestion.Description,
                    Color = suggestion.Color,
                    DirectoryPath = directoryPath
                });
                created++;
            }

            PersistCategoriesToSettings();
            _settingsStore.Save(_settings);
            UpdateCategoryMembership();
            ApplyAiCacheToItems();
            SetStatus($"Создано AI-категорий: {created}.");
        }
        catch (Exception ex)
        {
            ShowOperationError("Не удалось получить AI-подсказки категорий.", ex);
        }
    }

    private void SaveOrder_Click(object sender, RoutedEventArgs e)
    {
        ReadSettingsFromUi();

        if (Items.Count == 0)
        {
            SetStatus("Список пуст.");
            return;
        }

        if (!ConfirmSessionOperation(
                "Сохранить порядок",
                "Порядок вкладок будет записан в session.xml.",
                MessageBoxImage.Question))
        {
            return;
        }

        try
        {
            var sessionBackup = _journalStore.CreateSessionSnapshot(_settings.SessionPath, OperationKinds.SaveOrder);
            _sessionService.SaveOrder(_settings.SessionPath, Items);
            _journalStore.Add(new OperationJournalEntry
            {
                Kind = OperationKinds.SaveOrder,
                Description = "Сохранение порядка вкладок в session.xml",
                SessionPath = _settings.SessionPath,
                SessionBackupPath = sessionBackup
            });
            SetStatus("Порядок сохранен в session.xml.");
        }
        catch (Exception ex)
        {
            ShowOperationError("Не удалось сохранить порядок.", ex);
        }
    }

    private void CloseDetail_Click(object sender, RoutedEventArgs e)
    {
        CloseDetail();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ItemsView.Refresh();
        SetStatus(BuildCountStatus());
    }

    private void Filter_Changed(object sender, RoutedEventArgs e)
    {
        ItemsView.Refresh();
        SetStatus(BuildCountStatus());
    }

    private void ViewMode_Checked(object sender, RoutedEventArgs e)
    {
        ApplyViewMode();
    }

    private async void Items_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingSelection)
        {
            return;
        }

        var item = sender switch
        {
            WpfDataGrid dataGrid => dataGrid.SelectedItem as NoteFileItem,
            WpfListBox listBox => listBox.SelectedItem as NoteFileItem,
            _ => null
        };

        if (item is null)
        {
            return;
        }

        _syncingSelection = true;
        FilesGrid.SelectedItem = item;
        CardsList.SelectedItem = item;
        _syncingSelection = false;

        await OpenDetailAsync(item);
    }

    private async void CategoryItems_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingSelection)
        {
            return;
        }

        if ((sender as WpfListBox)?.SelectedItem is not NoteFileItem item)
        {
            return;
        }

        _syncingSelection = true;
        FilesGrid.SelectedItem = item;
        CardsList.SelectedItem = item;
        _syncingSelection = false;

        await OpenDetailAsync(item);
    }

    private async void ItemContainer_MouseEnter(object sender, WpfMouseEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: NoteFileItem item })
        {
            _aiCacheStore.ApplyToItem(item, Categories);
            ReadSettingsFromUi();
            if (_settings.AiAutoSummary)
            {
                await EnsureSummaryAsync(item);
            }
        }
    }

    private void DragSource_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(null);
        _draggedItem = FindItemFromSource(e.OriginalSource);
        _dragSourceKind = FindCategoryFromSource(e.OriginalSource) is null ? DragSourceFiles : DragSourceCategory;
    }

    private void DragSource_MouseMove(object sender, WpfMouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _draggedItem is null)
        {
            return;
        }

        var currentPosition = e.GetPosition(null);
        var movedFarEnough =
            Math.Abs(currentPosition.X - _dragStartPoint.X) > SystemParameters.MinimumHorizontalDragDistance
            || Math.Abs(currentPosition.Y - _dragStartPoint.Y) > SystemParameters.MinimumVerticalDragDistance;

        if (!movedFarEnough)
        {
            return;
        }

        var items = GetDragItems(_draggedItem, _dragSourceKind);
        var payload = new DragPayload(items, _dragSourceKind);
        System.Windows.DragDrop.DoDragDrop((DependencyObject)sender, payload, System.Windows.DragDropEffects.Move);
        _draggedItem = null;
    }

    private void Items_DragOver(object sender, WpfDragEventArgs e)
    {
        e.Effects = GetDragPayload(e) is null ? WpfDragDropEffects.None : WpfDragDropEffects.Move;
        e.Handled = true;
    }

    private async void Items_Drop(object sender, WpfDragEventArgs e)
    {
        var payload = GetDragPayload(e);
        if (payload is null)
        {
            return;
        }

        e.Handled = true;

        if (payload.SourceKind == DragSourceCategory)
        {
            await MoveItemsToGeneralAsync(payload.Items);
            return;
        }

        var targetItem = FindItemFromSource(e.OriginalSource);
        if (targetItem is null || payload.Items.Contains(targetItem))
        {
            return;
        }

        MoveItemsBeforeTarget(payload.Items, targetItem);
        SelectItems(payload.Items);
        SetStatus("Порядок изменен локально. Кнопка \"Порядок\" запишет его в session.xml.");
    }

    private void Category_DragOver(object sender, WpfDragEventArgs e)
    {
        e.Effects = GetDragPayload(e) is null ? WpfDragDropEffects.None : WpfDragDropEffects.Move;
        e.Handled = true;
    }

    private async void Category_Drop(object sender, WpfDragEventArgs e)
    {
        var payload = GetDragPayload(e);
        var category = FindCategoryFromSource(e.OriginalSource)
                       ?? (sender as FrameworkElement)?.DataContext as StorageCategory;

        if (payload is null || category is null)
        {
            return;
        }

        e.Handled = true;
        await MoveItemsToCategoryAsync(payload.Items, category);
    }
    private async Task RefreshAsync(string? preferredSessionKey = null)
    {
        _refreshCancellation?.Cancel();
        _refreshCancellation = new CancellationTokenSource();
        var cancellationToken = _refreshCancellation.Token;

        try
        {
            ReadSettingsFromUi();
            SetStatus("Читаю session.xml и временное хранилище Notepad++...");

            var previousSelectedKey = preferredSessionKey ?? _selectedItem?.SessionKey;
            var loadedItems = await _sessionService.LoadAsync(_settings.SessionPath, cancellationToken);

            Items.Clear();
            foreach (var item in loadedItems)
            {
                Items.Add(item);
            }

            UpdateCategoryMembership();
            ApplyAiCacheToItems();
            ApplyFileStateToItems();
            ComputeDuplicates();
            ItemsView.Refresh();
            SetStatus(BuildCountStatus());

            if (!string.IsNullOrWhiteSpace(previousSelectedKey))
            {
                var itemToSelect = Items.FirstOrDefault(item => item.SessionKey == previousSelectedKey);
                if (itemToSelect is not null)
                {
                    FilesGrid.SelectedItem = itemToSelect;
                    CardsList.SelectedItem = itemToSelect;
                    await OpenDetailAsync(itemToSelect);
                    return;
                }
            }

            CloseDetail();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            SetStatus("Ошибка чтения списка.");
            ShowOperationError("Не удалось прочитать список файлов Notepad++.", ex);
        }
    }

    private async Task OpenDetailAsync(NoteFileItem item)
    {
        _selectedItem = item;
        DetailPane.DataContext = item;
        DetailPane.Visibility = Visibility.Visible;
        DetailSplitter.Visibility = Visibility.Visible;
        SplitterColumn.Width = new GridLength(6);
        DetailColumn.Width = new GridLength(Math.Max(480, Math.Min(680, ActualWidth * 0.43)));
        SavePermanentButton.IsEnabled = item.CanSaveAsPermanent;
        MakeTemporaryButton.IsEnabled = item.CanConvertToTemporary;

        item.FullContent = "Загрузка...";
        try
        {
            item.FullContent = await FileTextReader.ReadAllTextAsync(item.ContentPath);
        }
        catch (Exception ex)
        {
            item.FullContent = $"Не удалось прочитать файл: {ex.Message}";
        }

        ReadSettingsFromUi();
        if (_settings.AiAutoSummary || !string.IsNullOrWhiteSpace(item.Summary))
        {
            await EnsureSummaryAsync(item);
        }
    }

    private void CloseDetail()
    {
        _selectedItem = null;
        DetailPane.DataContext = null;
        DetailPane.Visibility = Visibility.Collapsed;
        DetailSplitter.Visibility = Visibility.Collapsed;
        SplitterColumn.Width = new GridLength(0);
        DetailColumn.Width = new GridLength(0);

        _syncingSelection = true;
        FilesGrid.SelectedItems.Clear();
        CardsList.SelectedItems.Clear();
        FilesGrid.SelectedItem = null;
        CardsList.SelectedItem = null;
        _syncingSelection = false;
    }

    private async Task EnsureSummaryAsync(NoteFileItem item, bool force = false)
    {
        ReadSettingsFromUi();

        if (!_settings.AiEnabled)
        {
            if (force)
            {
                item.SummaryState = "AI выключен.";
            }

            return;
        }

        if (!force && !string.IsNullOrWhiteSpace(item.Summary))
        {
            return;
        }

        if (item.IsSummaryLoading)
        {
            return;
        }

        if (_summaryTasks.TryGetValue(item.SessionKey, out var runningTask) && !runningTask.IsCompleted)
        {
            await runningTask;
            return;
        }

        var task = RunSummaryAsync(item);
        _summaryTasks[item.SessionKey] = task;
        await task;
    }

    private async Task RunSummaryAsync(NoteFileItem item)
    {
        item.IsSummaryLoading = true;
        item.SummaryState = "AI считает описание...";

        try
        {
            item.Summary = await _aiSummaryService.SummarizeAsync(item, _settings);
            item.SummaryState = string.Empty;
            _aiCacheStore.SaveSummary(item);
        }
        catch (Exception ex)
        {
            item.SummaryState = ex.Message;
        }
        finally
        {
            item.IsSummaryLoading = false;
        }
    }

    private async Task RenameItemAsync(NoteFileItem item)
    {
        if (!item.Exists)
        {
            SetStatus("Нельзя переименовать отсутствующий файл.");
            return;
        }

        if (!EnsureItemsUnlocked(new[] { item }, "переименовать"))
        {
            return;
        }

        var dialog = new RenameFileDialog(item.DisplayName)
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        if (!ConfirmSessionOperation(
                "Переименовать файл",
                $"Файл будет физически переименован, а путь будет обновлен в session.xml.\n\nФайл: {item.DisplayName}\nНовое имя: {dialog.NewFileName}",
                MessageBoxImage.Question))
        {
            return;
        }

        try
        {
            ReadSettingsFromUi();
            var sourcePath = item.ContentPath;
            var sessionBackup = _journalStore.CreateSessionSnapshot(_settings.SessionPath, OperationKinds.Rename);
            var targetPath = await _sessionService.RenameItemAsync(item, dialog.NewFileName);
            _journalStore.Add(new OperationJournalEntry
            {
                Kind = OperationKinds.Rename,
                Description = $"Переименование файла: {item.DisplayName} -> {Path.GetFileName(targetPath)}",
                SessionPath = _settings.SessionPath,
                SessionBackupPath = sessionBackup,
                Files =
                {
                    new OperationFileChange
                    {
                        Action = OperationFileActions.Rename,
                        DisplayName = item.DisplayName,
                        SourcePath = sourcePath,
                        TargetPath = targetPath
                    }
                }
            });
            SetStatus($"Файл переименован: {dialog.NewFileName}");
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            ShowOperationError("Не удалось переименовать файл.", ex);
        }
    }
    private void MoveSelectedBy(int delta)
    {
        var items = GetSelectedItems()
            .Where(item => Items.Contains(item))
            .Distinct()
            .OrderBy(item => Items.IndexOf(item))
            .ToList();
        if (items.Count == 0)
        {
            SetStatus("Выберите файл для перемещения.");
            return;
        }

        if (delta < 0)
        {
            if (Items.IndexOf(items[0]) == 0)
            {
                return;
            }

            foreach (var item in items)
            {
                var index = Items.IndexOf(item);
                Items.Move(index, Math.Max(0, index - 1));
            }
        }
        else
        {
            if (Items.IndexOf(items[^1]) == Items.Count - 1)
            {
                return;
            }

            for (var i = items.Count - 1; i >= 0; i--)
            {
                var item = items[i];
                var index = Items.IndexOf(item);
                Items.Move(index, Math.Min(Items.Count - 1, index + 1));
            }
        }

        ItemsView.Refresh();
        SelectItems(items);
        SetStatus("Порядок изменен локально. Кнопка \"Порядок\" запишет его в session.xml.");
    }
    private async Task DeleteItemsAsync(IReadOnlyList<NoteFileItem> selectedItems)
    {
        var items = selectedItems.Distinct().ToList();
        if (items.Count == 0)
        {
            SetStatus("Выберите файл для удаления.");
            return;
        }

        if (!EnsureItemsUnlocked(items, "удалить"))
        {
            return;
        }

        ReadSettingsFromUi();

        if (_settings.TrashMode == TrashModes.CustomFolder && string.IsNullOrWhiteSpace(_settings.TrashFolderPath))
        {
            var selected = BrowseFolder("Папка кастомной корзины", _settings.TrashFolderPath);
            if (string.IsNullOrWhiteSpace(selected))
            {
                return;
            }

            _settings.TrashFolderPath = selected;
            _settings.TrashMode = TrashModes.CustomFolder;
            _settingsStore.Save(_settings);
        }

        var destination = _settings.TrashMode == TrashModes.CustomFolder
            ? _settings.TrashFolderPath
            : "системная корзина Windows";
        var existingCount = items.Count(item => item.Exists);
        var missingCount = items.Count - existingCount;
        var sample = string.Join(Environment.NewLine, items.Take(8).Select(item => "- " + item.DisplayName));
        if (items.Count > 8)
        {
            sample += Environment.NewLine + $"... и еще {items.Count - 8}";
        }

        var message = $"Файлов: {items.Count}\nНайдено на диске: {existingCount}\nОтсутствующих записей: {missingCount}\nКорзина: {destination}\n\n{sample}";
        if (!ConfirmSessionOperation("Удалить файлы", message, MessageBoxImage.Warning))
        {
            return;
        }

        try
        {
            var options = new TrashOptions
            {
                Mode = _settings.TrashMode,
                CustomFolderPath = _settings.TrashFolderPath
            };
            var sessionBackup = _journalStore.CreateSessionSnapshot(_settings.SessionPath, OperationKinds.Delete);
            var entry = new OperationJournalEntry
            {
                Kind = OperationKinds.Delete,
                Description = items.Count == 1 ? $"Удаление файла: {items[0].DisplayName}" : $"Удаление файлов: {items.Count}",
                SessionPath = _settings.SessionPath,
                SessionBackupPath = sessionBackup
            };

            foreach (var item in items)
            {
                var sourcePath = item.ContentPath;
                var targetPath = await _sessionService.DeleteItemAsync(item, options);
                entry.Files.Add(new OperationFileChange
                {
                    Action = _settings.TrashMode == TrashModes.CustomFolder ? OperationFileActions.DeleteToCustomTrash : OperationFileActions.DeleteToRecycleBin,
                    DisplayName = item.DisplayName,
                    SourcePath = sourcePath,
                    TargetPath = targetPath ?? string.Empty
                });
            }

            _journalStore.Add(entry);
            SetStatus(items.Count == 1 ? $"Удалено из списка: {items[0].DisplayName}" : $"Удалено из списка: {items.Count}");
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            ShowOperationError("Не удалось удалить файл.", ex);
        }
    }
    private async Task MoveItemsToCategoryAsync(IReadOnlyList<NoteFileItem> selectedItems, StorageCategory category)
    {
        ReadSettingsFromUi();

        var items = selectedItems
            .Where(item => item.Exists && !IsUnderDirectory(item.ContentPath, category.DirectoryPath))
            .Distinct()
            .ToList();
        if (items.Count == 0)
        {
            SetStatus($"Файлы уже в категории: {category.Name}");
            return;
        }

        if (!EnsureItemsUnlocked(items, "переместить в категорию"))
        {
            return;
        }

        var sample = string.Join(Environment.NewLine, items.Take(8).Select(item => "- " + item.DisplayName));
        if (items.Count > 8)
        {
            sample += Environment.NewLine + $"... и еще {items.Count - 8}";
        }

        if (!ConfirmSessionOperation(
                "Переместить в категорию",
                $"Файлы будут физически перемещены, а пути будут обновлены в session.xml.\n\nКатегория: {category.Name}\nПапка: {category.DirectoryPath}\nФайлов: {items.Count}\n\n{sample}",
                MessageBoxImage.Question))
        {
            return;
        }

        try
        {
            var sessionBackup = _journalStore.CreateSessionSnapshot(_settings.SessionPath, OperationKinds.Move);
            var entry = new OperationJournalEntry
            {
                Kind = OperationKinds.Move,
                Description = items.Count == 1 ? $"Перенос в категорию {category.Name}: {items[0].DisplayName}" : $"Перенос в категорию {category.Name}: {items.Count}",
                SessionPath = _settings.SessionPath,
                SessionBackupPath = sessionBackup
            };

            foreach (var item in items)
            {
                var sourcePath = item.ContentPath;
                var targetPath = await _sessionService.MoveItemToDirectoryAsync(item, category.DirectoryPath);
                entry.Files.Add(new OperationFileChange
                {
                    Action = OperationFileActions.Move,
                    DisplayName = item.DisplayName,
                    SourcePath = sourcePath,
                    TargetPath = targetPath
                });
            }

            _journalStore.Add(entry);
            SetStatus(items.Count == 1 ? $"Перемещено в категорию {category.Name}: {items[0].DisplayName}" : $"Перемещено в категорию {category.Name}: {items.Count}");
            await RefreshAsync(items.LastOrDefault()?.SessionKey);
        }
        catch (Exception ex)
        {
            ShowOperationError("Не удалось переместить файл в категорию.", ex);
        }
    }

    private async Task MoveItemsToGeneralAsync(IReadOnlyList<NoteFileItem> selectedItems)
    {
        ReadSettingsFromUi();

        Directory.CreateDirectory(_settings.CategoryRootPath);
        var items = selectedItems
            .Where(item => item.Exists && GetItemCategory(item) is not null)
            .Distinct()
            .ToList();
        if (items.Count == 0)
        {
            SetStatus("Файлы уже в общем списке.");
            return;
        }

        if (!EnsureItemsUnlocked(items, "вернуть в общий список"))
        {
            return;
        }

        var sample = string.Join(Environment.NewLine, items.Take(8).Select(item => "- " + item.DisplayName));
        if (items.Count > 8)
        {
            sample += Environment.NewLine + $"... и еще {items.Count - 8}";
        }

        if (!ConfirmSessionOperation(
                "Вернуть в общий список",
                $"Файлы будут вынесены из категорий в общий корень, а пути будут обновлены в session.xml.\n\nПапка: {_settings.CategoryRootPath}\nФайлов: {items.Count}\n\n{sample}",
                MessageBoxImage.Question))
        {
            return;
        }

        try
        {
            var sessionBackup = _journalStore.CreateSessionSnapshot(_settings.SessionPath, OperationKinds.Move);
            var entry = new OperationJournalEntry
            {
                Kind = OperationKinds.Move,
                Description = items.Count == 1 ? $"Возврат в общий список: {items[0].DisplayName}" : $"Возврат в общий список: {items.Count}",
                SessionPath = _settings.SessionPath,
                SessionBackupPath = sessionBackup
            };

            foreach (var item in items)
            {
                var sourcePath = item.ContentPath;
                var targetPath = await _sessionService.MoveItemToDirectoryAsync(item, _settings.CategoryRootPath);
                entry.Files.Add(new OperationFileChange
                {
                    Action = OperationFileActions.Move,
                    DisplayName = item.DisplayName,
                    SourcePath = sourcePath,
                    TargetPath = targetPath
                });
            }

            _journalStore.Add(entry);
            SetStatus(items.Count == 1 ? $"Файл возвращен в общий список: {items[0].DisplayName}" : $"Файлы возвращены в общий список: {items.Count}");
            await RefreshAsync(items.LastOrDefault()?.SessionKey);
        }
        catch (Exception ex)
        {
            ShowOperationError("Не удалось вернуть файл в общий список.", ex);
        }
    }
    private bool FilterItem(object value)
    {
        if (value is not NoteFileItem item)
        {
            return false;
        }

        if (TempOnlyBox?.IsChecked == true && !item.IsTemporary)
        {
            return false;
        }

        if (UnsortedOnlyBox?.IsChecked == true && item.IsCategorized)
        {
            return false;
        }

        if (DuplicatesOnlyBox?.IsChecked == true && !item.IsDuplicate)
        {
            return false;
        }

        if (FavoritesOnlyBox?.IsChecked == true && !item.IsFavorite)
        {
            return false;
        }

        var query = SearchBox?.Text?.Trim();
        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        return Contains(item.DisplayName, query)
               || Contains(item.PathText, query)
               || Contains(item.StorageText, query)
               || Contains(item.PreviewText, query)
               || Contains(item.Summary, query)
               || Contains(item.CategoryText, query)
               || Contains(item.TagsText, query)
               || Contains(item.FlagsText, query)
               || Contains(item.DuplicateText, query);
    }

    private void ReadSettingsFromUi()
    {
        EnsureSettingsDefaults();
    }

    private void EnsureSettingsDefaults()
    {
        if (string.IsNullOrWhiteSpace(_settings.SessionPath))
        {
            _settings.SessionPath = NotepadPlusPlusSessionService.GetDefaultSessionPath();
        }

        if (string.IsNullOrWhiteSpace(_settings.CategoryRootPath))
        {
            var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            _settings.CategoryRootPath = Path.Combine(documents, "TempNoteManager Categories");
        }

        if (string.IsNullOrWhiteSpace(_settings.TrashFolderPath))
        {
            _settings.TrashFolderPath = Path.Combine(_settings.CategoryRootPath, "_Trash");
        }

        if (string.IsNullOrWhiteSpace(_settings.TrashMode))
        {
            _settings.TrashMode = TrashModes.RecycleBin;
        }
    }
    private void LoadCategoriesFromSettings()
    {
        Categories.Clear();

        foreach (var category in _settings.Categories)
        {
            if (!string.IsNullOrWhiteSpace(category.DirectoryPath))
            {
                Directory.CreateDirectory(category.DirectoryPath);
            }

            Categories.Add(category);
        }
    }

    private void PersistCategoriesToSettings()
    {
        _settings.Categories = Categories
            .Select(category => new StorageCategory
            {
                Id = category.Id,
                Name = category.Name,
                Description = category.Description,
                DirectoryPath = category.DirectoryPath,
                Color = category.Color
            })
            .ToList();
    }

    private void UpdateCategoryMembership()
    {
        foreach (var category in Categories)
        {
            category.Items.Clear();
        }

        foreach (var item in Items)
        {
            var category = GetItemCategory(item);
            item.CategoryName = category?.Name ?? string.Empty;
            item.CategoryColor = category?.Color ?? string.Empty;

            if (category is not null)
            {
                category.Items.Add(item);
            }
        }

        foreach (var category in Categories)
        {
            category.NotifyItemsChanged();
        }
    }

    private void ApplyAiCacheToItems()
    {
        foreach (var item in Items)
        {
            _aiCacheStore.ApplyToItem(item, Categories);
        }
    }

    private void ApplyFileStateToItems()
    {
        foreach (var item in Items)
        {
            _fileStateStore.ApplyToItem(item);
        }
    }

    private void ComputeDuplicates()
    {
        foreach (var item in Items)
        {
            item.DuplicateGroupKey = string.Empty;
            item.DuplicateText = string.Empty;
        }

        var duplicateGroups = Items
            .Where(item => !string.IsNullOrWhiteSpace(item.ContentFingerprint))
            .GroupBy(item => item.ContentFingerprint, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .ToList();

        var groupIndex = 1;
        foreach (var group in duplicateGroups)
        {
            var key = $"D{groupIndex}";
            var count = group.Count();
            foreach (var item in group)
            {
                item.DuplicateGroupKey = key;
                item.DuplicateText = $"{key} x{count}";
            }

            groupIndex++;
        }
    }

    private void ApplyViewMode()
    {
        if (FilesGrid is null || CardsList is null)
        {
            return;
        }

        var showCards = CardsViewButton?.IsChecked == true;
        CardsList.Visibility = showCards ? Visibility.Visible : Visibility.Collapsed;
        FilesGrid.Visibility = showCards ? Visibility.Collapsed : Visibility.Visible;
    }

    private NoteFileItem? GetSelectedItem()
    {
        return FilesGrid.SelectedItem as NoteFileItem ?? CardsList.SelectedItem as NoteFileItem ?? _selectedItem;
    }

    private IReadOnlyList<NoteFileItem> GetSelectedItems()
    {
        var selected = FilesGrid.SelectedItems.OfType<NoteFileItem>()
            .Concat(CardsList.SelectedItems.OfType<NoteFileItem>())
            .Distinct()
            .ToList();

        if (selected.Count == 0 && _selectedItem is not null)
        {
            selected.Add(_selectedItem);
        }

        return selected;
    }

    private IReadOnlyList<NoteFileItem> GetDragItems(NoteFileItem draggedItem, string sourceKind)
    {
        if (sourceKind == DragSourceFiles)
        {
            var selected = GetSelectedItems();
            if (selected.Contains(draggedItem))
            {
                return selected;
            }
        }

        return new[] { draggedItem };
    }

    private void SelectItems(IReadOnlyList<NoteFileItem> items)
    {
        if (items.Count == 0)
        {
            return;
        }

        _syncingSelection = true;
        FilesGrid.SelectedItems.Clear();
        CardsList.SelectedItems.Clear();

        foreach (var item in items)
        {
            FilesGrid.SelectedItems.Add(item);
            CardsList.SelectedItems.Add(item);
        }

        FilesGrid.SelectedItem = items[0];
        CardsList.SelectedItem = items[0];
        _syncingSelection = false;
    }

    private StorageCategory? GetItemCategory(NoteFileItem item)
    {
        return Categories.FirstOrDefault(category =>
            IsUnderDirectory(item.ContentPath, category.DirectoryPath)
            || IsUnderDirectory(item.PathText, category.DirectoryPath));
    }

    private string BuildCountStatus()
    {
        var visibleCount = ItemsView.Cast<object>().Count();
        var temporaryCount = Items.Count(item => item.IsTemporary);
        var snapshotCount = Items.Count(item => item.IsInTemporaryStorage && !item.IsTemporary);
        var categorizedCount = Items.Count(item => item.IsCategorized);
        var duplicateCount = Items.Count(item => item.IsDuplicate);
        var favoriteCount = Items.Count(item => item.IsFavorite);
        var lockedCount = Items.Count(item => item.IsLocked);
        return $"Файлов: {Items.Count}; видно: {visibleCount}; временных: {temporaryCount}; снимков: {snapshotCount}; в категориях: {categorizedCount}; дубликатов: {duplicateCount}; избранных: {favoriteCount}; заблокировано: {lockedCount}.";
    }

    private void SetStatus(string text)
    {
        StatusTextBlock.Text = text;
    }

    private static string FormatYesNo(bool value)
    {
        return value ? "да" : "нет";
    }

    private void ShowOperationError(string title, Exception exception)
    {
        SetStatus(exception.Message);
        WpfMessageBox.Show(this, exception.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private async Task UndoLastOperationAsync()
    {
        var entry = _journalStore.GetLastUndoable();
        if (entry is null)
        {
            SetStatus("Нет операции для отмены.");
            return;
        }

        var result = WpfMessageBox.Show(
            this,
            $"Откатить последнюю операцию?\n\n{entry.Description}\n{entry.CreatedAt.LocalDateTime:yyyy-MM-dd HH:mm:ss}",
            "Отмена операции",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            var undoResult = await _journalStore.UndoAsync(entry);
            await RefreshAsync();

            var message = $"Операция отменена. session.xml восстановлен: {FormatYesNo(undoResult.RestoredSession)}; файлов возвращено: {undoResult.RestoredFiles}.";
            if (undoResult.Warnings.Count > 0)
            {
                message += Environment.NewLine + Environment.NewLine + string.Join(Environment.NewLine, undoResult.Warnings);
            }

            SetStatus(message.Replace(Environment.NewLine, " "));
            WpfMessageBox.Show(this, message, "Отмена операции", MessageBoxButton.OK, undoResult.Warnings.Count == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            ShowOperationError("Не удалось отменить операцию.", ex);
        }
    }

    private bool ConfirmSessionOperation(string title, string message, MessageBoxImage image)
    {
        var finalMessage = message;
        if (IsNotepadPlusPlusRunning())
        {
            finalMessage += "\n\nВнимание: Notepad++ сейчас запущен. Лучше закрыть его перед изменением файлов и session.xml, иначе Notepad++ может перезаписать список вкладок.";
        }

        return WpfMessageBox.Show(this, finalMessage, title, MessageBoxButton.YesNo, image) == MessageBoxResult.Yes;
    }

    private bool ConfirmAiOperation(string title, string message)
    {
        return WpfMessageBox.Show(this, message, title, MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes;
    }

    private bool EnsureItemsUnlocked(IReadOnlyList<NoteFileItem> items, string operationName)
    {
        var lockedItems = items.Where(item => item.IsLocked).ToList();
        if (lockedItems.Count == 0)
        {
            return true;
        }

        var sample = string.Join(Environment.NewLine, lockedItems.Take(8).Select(item => "- " + item.DisplayName));
        if (lockedItems.Count > 8)
        {
            sample += Environment.NewLine + $"... и еще {lockedItems.Count - 8}";
        }

        var result = WpfMessageBox.Show(
            this,
            $"Операция \"{operationName}\" затрагивает заблокированные файлы. Снять блокировку и продолжить?\n\n{sample}",
            "Файлы заблокированы",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
        {
            return false;
        }

        foreach (var item in lockedItems)
        {
            item.IsLocked = false;
            _fileStateStore.SaveItem(item);
        }

        ItemsView.Refresh();
        return true;
    }

    private void ToggleStateForSelected(Func<NoteFileItem, bool> getter, Action<NoteFileItem, bool> setter, string label)
    {
        var items = GetSelectedItems();
        if (items.Count == 0)
        {
            SetStatus("Выберите файлы.");
            return;
        }

        var newValue = items.Any(item => !getter(item));
        foreach (var item in items)
        {
            setter(item, newValue);
            _fileStateStore.SaveItem(item);
        }

        ItemsView.Refresh();
        SetStatus(newValue ? $"Включено: {label}; файлов: {items.Count}." : $"Выключено: {label}; файлов: {items.Count}.");
    }

    private static bool IsNotepadPlusPlusRunning()
    {
        try
        {
            return Process.GetProcessesByName("notepad++").Length > 0;
        }
        catch
        {
            return false;
        }
    }

    private DragPayload? GetDragPayload(WpfDragEventArgs e)
    {
        if (e.Data.GetData(typeof(DragPayload)) is DragPayload payload)
        {
            return payload;
        }

        if (e.Data.GetData(typeof(NoteFileItem)) is NoteFileItem item)
        {
            return new DragPayload(new[] { item }, DragSourceFiles);
        }

        return null;
    }

    private void MoveItemsBeforeTarget(IReadOnlyList<NoteFileItem> draggedItems, NoteFileItem targetItem)
    {
        var movingItems = draggedItems
            .Where(item => Items.Contains(item) && !ReferenceEquals(item, targetItem))
            .Distinct()
            .ToList();
        if (movingItems.Count == 0 || !Items.Contains(targetItem))
        {
            return;
        }

        foreach (var item in movingItems)
        {
            Items.Remove(item);
        }

        var targetIndex = Items.IndexOf(targetItem);
        if (targetIndex < 0)
        {
            targetIndex = Items.Count;
        }

        for (var index = 0; index < movingItems.Count; index++)
        {
            Items.Insert(targetIndex + index, movingItems[index]);
        }

        ItemsView.Refresh();
    }
    private static bool Contains(string source, string query)
    {
        return source?.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string? GetExistingDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        return Directory.Exists(path) ? path : null;
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

    private static string BuildSafeFileName(string source)
    {
        var name = string.IsNullOrWhiteSpace(source) ? "note.txt" : source.Trim();

        foreach (var invalidChar in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalidChar, '_');
        }

        if (string.IsNullOrWhiteSpace(Path.GetExtension(name)))
        {
            name += ".txt";
        }

        return name;
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

    private static string MakeSafeName(string source)
    {
        var name = source.Trim();
        foreach (var invalidChar in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalidChar, '_');
        }

        return name;
    }

    private static string GetUniqueDirectoryPath(string rootPath, string categoryName)
    {
        var directoryName = MakeSafeName(categoryName);

        var path = Path.Combine(rootPath, directoryName);
        var index = 2;

        while (Directory.Exists(path))
        {
            path = Path.Combine(rootPath, $"{directoryName} ({index})");
            index++;
        }

        return path;
    }

    private static bool IsUnderDirectory(string path, string directory)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(directory))
        {
            return false;
        }

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

    private static NoteFileItem? FindItemFromSource(object source)
    {
        var dependencyObject = source as DependencyObject;

        while (dependencyObject is not null)
        {
            if (dependencyObject is FrameworkElement { DataContext: NoteFileItem item })
            {
                return item;
            }

            dependencyObject = VisualTreeHelper.GetParent(dependencyObject);
        }

        return null;
    }

    private static StorageCategory? FindCategoryFromSource(object source)
    {
        var dependencyObject = source as DependencyObject;

        while (dependencyObject is not null)
        {
            if (dependencyObject is FrameworkElement { DataContext: StorageCategory category })
            {
                return category;
            }

            dependencyObject = VisualTreeHelper.GetParent(dependencyObject);
        }

        return null;
    }

    private sealed record DragPayload(IReadOnlyList<NoteFileItem> Items, string SourceKind);
}
