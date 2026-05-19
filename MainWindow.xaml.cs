using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using TempNoteManager.Models;
using TempNoteManager.Services;
using Forms = System.Windows.Forms;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
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
        ApplySettingsToUi();
        LoadCategoriesFromSettings();
        ApplyViewMode();
        await RefreshAsync();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        await RefreshAsync();
    }

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
        var selected = BrowseFolder("Папка кастомной корзины", TrashFolderBox.Text);
        if (!string.IsNullOrWhiteSpace(selected))
        {
            TrashFolderBox.Text = selected;
            TrashModeBox.SelectedIndex = 1;
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

    private void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ReadSettingsFromUi();
            PersistCategoriesToSettings();
            _settingsStore.Save(_settings);

            if (string.IsNullOrWhiteSpace(_settings.AiApiKey))
            {
                _settingsStore.DeleteAiApiKey();
                SetStatus("Настройки сохранены. AI key удален из Windows Credential Manager.");
            }
            else
            {
                _settingsStore.SaveAiApiKey(_settings.AiApiKey);
                SetStatus("Настройки сохранены. AI key записан в Windows Credential Manager.");
            }
        }
        catch (Exception ex)
        {
            ShowOperationError("Не удалось сохранить настройки.", ex);
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
        ItemsView.Refresh();
        SetStatus($"Категория обновлена: {category.Name}");
    }

    private async void DeleteSelected_Click(object sender, RoutedEventArgs e)
    {
        var item = GetSelectedItem();
        if (item is null)
        {
            SetStatus("Выберите файл для удаления.");
            return;
        }

        await DeleteItemAsync(item);
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

        try
        {
            ReadSettingsFromUi();
            await _sessionService.SaveTemporaryAsPermanentAsync(item, dialog.FileName);
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

        var result = WpfMessageBox.Show(
            this,
            "Будет создана временная копия в backup-папке Notepad++ и обновлен session.xml. Перед операцией лучше закрыть Notepad++.",
            "Сделать файл временным",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            await _sessionService.ConvertPermanentToTemporaryAsync(item);
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

        var visibleItems = ItemsView.Cast<NoteFileItem>().Where(item => item.Exists).ToList();
        if (visibleItems.Count == 0)
        {
            SetStatus("Нет видимых файлов для AI-разметки.");
            return;
        }

        var analyzed = 0;
        var fromCache = 0;
        foreach (var item in visibleItems)
        {
            var categoriesToAnalyze = _aiCacheStore.GetCategoriesNeedingClassification(item, Categories);
            if (categoriesToAnalyze.Count == 0)
            {
                _aiCacheStore.ApplyToItem(item, Categories);
                fromCache++;
                continue;
            }

            analyzed++;
            SetStatus($"AI размечает {analyzed}: {item.DisplayName}; новых/измененных категорий: {categoriesToAnalyze.Count}");

            try
            {
                var tags = await _aiSummaryService.ClassifyAsync(item, categoriesToAnalyze, _settings);
                _aiCacheStore.SaveClassification(item, categoriesToAnalyze, tags, Categories);
            }
            catch (Exception ex)
            {
                item.SummaryState = ex.Message;
            }
        }

        ItemsView.Refresh();
        SetStatus($"AI-разметка готова: AI-запросов {analyzed}; из кэша {fromCache}.");
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

        var result = WpfMessageBox.Show(
            this,
            "Порядок вкладок будет записан в session.xml. Перед операцией лучше закрыть Notepad++.",
            "Сохранить порядок",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            _sessionService.SaveOrder(_settings.SessionPath, Items);
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
            await EnsureSummaryAsync(item);
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

        var payload = new DragPayload(_draggedItem, _dragSourceKind);
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
            await MoveItemToGeneralAsync(payload.Item);
            return;
        }

        var targetItem = FindItemFromSource(e.OriginalSource);
        if (targetItem is null || ReferenceEquals(targetItem, payload.Item))
        {
            return;
        }

        MoveItemBeforeTarget(payload.Item, targetItem);
        FilesGrid.SelectedItem = payload.Item;
        CardsList.SelectedItem = payload.Item;
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
        await MoveItemToCategoryAsync(payload.Item, category);
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

        await EnsureSummaryAsync(item);
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

        var dialog = new RenameFileDialog(item.DisplayName)
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var result = WpfMessageBox.Show(
            this,
            $"Файл будет физически переименован, а путь будет обновлен в session.xml.\n\nФайл: {item.DisplayName}\nНовое имя: {dialog.NewFileName}\n\nПеред операцией лучше закрыть Notepad++.",
            "Переименовать файл",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            await _sessionService.RenameItemAsync(item, dialog.NewFileName);
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
        var item = GetSelectedItem();
        if (item is null)
        {
            SetStatus("Выберите файл для перемещения.");
            return;
        }

        var oldIndex = Items.IndexOf(item);
        if (oldIndex < 0)
        {
            return;
        }

        var newIndex = Math.Clamp(oldIndex + delta, 0, Items.Count - 1);
        if (newIndex == oldIndex)
        {
            return;
        }

        Items.Move(oldIndex, newIndex);
        ItemsView.Refresh();

        _syncingSelection = true;
        FilesGrid.SelectedItem = item;
        CardsList.SelectedItem = item;
        _syncingSelection = false;

        SetStatus("Порядок изменен локально. Кнопка \"Порядок\" запишет его в session.xml.");
    }

    private async Task DeleteItemAsync(NoteFileItem item)
    {
        ReadSettingsFromUi();

        if (_settings.TrashMode == TrashModes.CustomFolder && string.IsNullOrWhiteSpace(_settings.TrashFolderPath))
        {
            var selected = BrowseFolder("Папка кастомной корзины", _settings.TrashFolderPath);
            if (string.IsNullOrWhiteSpace(selected))
            {
                return;
            }

            TrashFolderBox.Text = selected;
            _settings.TrashFolderPath = selected;
        }

        var destination = _settings.TrashMode == TrashModes.CustomFolder
            ? _settings.TrashFolderPath
            : "системная корзина Windows";

        var message = item.Exists
            ? $"Файл будет удален из списка Notepad++ и перемещен в корзину.\n\nФайл: {item.DisplayName}\nПуть: {item.ContentPath}\nКорзина: {destination}\n\nПеред операцией лучше закрыть Notepad++."
            : $"Файл не найден на диске. Удалить запись из session.xml?\n\nФайл: {item.DisplayName}";

        var result = WpfMessageBox.Show(
            this,
            message,
            "Удалить файл",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
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

            await _sessionService.DeleteItemAsync(item, options);
            SetStatus($"Удалено из списка: {item.DisplayName}");
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            ShowOperationError("Не удалось удалить файл.", ex);
        }
    }

    private async Task MoveItemToCategoryAsync(NoteFileItem item, StorageCategory category)
    {
        ReadSettingsFromUi();

        if (IsUnderDirectory(item.ContentPath, category.DirectoryPath))
        {
            SetStatus($"Файл уже в категории: {category.Name}");
            return;
        }

        var result = WpfMessageBox.Show(
            this,
            $"Файл будет физически перемещен в категорию и путь будет обновлен в session.xml.\n\nФайл: {item.DisplayName}\nКатегория: {category.Name}\nПапка: {category.DirectoryPath}\n\nПеред операцией лучше закрыть Notepad++.",
            "Переместить в категорию",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            await _sessionService.MoveItemToDirectoryAsync(item, category.DirectoryPath);
            SetStatus($"Перемещено в категорию {category.Name}: {item.DisplayName}");
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            ShowOperationError("Не удалось переместить файл в категорию.", ex);
        }
    }

    private async Task MoveItemToGeneralAsync(NoteFileItem item)
    {
        ReadSettingsFromUi();

        var category = GetItemCategory(item);
        if (category is null)
        {
            SetStatus("Файл уже в общем списке.");
            return;
        }

        Directory.CreateDirectory(_settings.CategoryRootPath);
        var result = WpfMessageBox.Show(
            this,
            $"Файл будет вынесен из категории в общий корень категорий и путь будет обновлен в session.xml.\n\nФайл: {item.DisplayName}\nИз категории: {category.Name}\nПапка: {_settings.CategoryRootPath}",
            "Вернуть в общий список",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            await _sessionService.MoveItemToDirectoryAsync(item, _settings.CategoryRootPath);
            SetStatus($"Файл возвращен в общий список: {item.DisplayName}");
            await RefreshAsync();
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
               || Contains(item.TagsText, query);
    }

    private void ApplySettingsToUi()
    {
        SessionPathBox.Text = _settings.SessionPath;
        AiEnabledBox.IsChecked = _settings.AiEnabled;
        AiEndpointBox.Text = _settings.AiEndpoint;
        AiModelBox.Text = _settings.AiModel;
        AiKeyBox.Password = _settings.AiApiKey;
        TrashModeBox.SelectedIndex = _settings.TrashMode == TrashModes.CustomFolder ? 1 : 0;
        TrashFolderBox.Text = _settings.TrashFolderPath;
        CategoryRootBox.Text = _settings.CategoryRootPath;
    }

    private void ReadSettingsFromUi()
    {
        _settings.SessionPath = SessionPathBox.Text.Trim();
        _settings.AiEnabled = AiEnabledBox.IsChecked == true;
        _settings.AiEndpoint = AiEndpointBox.Text.Trim();
        _settings.AiModel = AiModelBox.Text.Trim();
        _settings.AiApiKey = AiKeyBox.Password.Trim();
        _settings.TrashMode = (TrashModeBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? TrashModes.RecycleBin;
        _settings.TrashFolderPath = TrashFolderBox.Text.Trim();
        _settings.CategoryRootPath = CategoryRootBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(_settings.CategoryRootPath))
        {
            var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            _settings.CategoryRootPath = Path.Combine(documents, "TempNoteManager Categories");
            CategoryRootBox.Text = _settings.CategoryRootPath;
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
        return _selectedItem ?? FilesGrid.SelectedItem as NoteFileItem ?? CardsList.SelectedItem as NoteFileItem;
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
        return $"Файлов: {Items.Count}; видно: {visibleCount}; временных: {temporaryCount}; snapshot: {snapshotCount}; в категориях: {categorizedCount}.";
    }

    private void SetStatus(string text)
    {
        StatusTextBlock.Text = text;
    }

    private void ShowOperationError(string title, Exception exception)
    {
        SetStatus(exception.Message);
        WpfMessageBox.Show(this, exception.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private DragPayload? GetDragPayload(WpfDragEventArgs e)
    {
        if (e.Data.GetData(typeof(DragPayload)) is DragPayload payload)
        {
            return payload;
        }

        if (e.Data.GetData(typeof(NoteFileItem)) is NoteFileItem item)
        {
            return new DragPayload(item, DragSourceFiles);
        }

        return null;
    }

    private void MoveItemBeforeTarget(NoteFileItem draggedItem, NoteFileItem targetItem)
    {
        var oldIndex = Items.IndexOf(draggedItem);
        var targetIndex = Items.IndexOf(targetItem);
        if (oldIndex < 0 || targetIndex < 0 || oldIndex == targetIndex)
        {
            return;
        }

        var newIndex = targetIndex;
        if (oldIndex < targetIndex)
        {
            newIndex--;
        }

        if (oldIndex != newIndex)
        {
            Items.Move(oldIndex, newIndex);
            ItemsView.Refresh();
        }
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

    private sealed record DragPayload(NoteFileItem Item, string SourceKind);
}
