using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TempNoteManager.Models;
using TempNoteManager.Services;
using WpfMessageBox = System.Windows.MessageBox;
using WpfBrush = System.Windows.Media.Brush;
using WpfBrushes = System.Windows.Media.Brushes;

namespace TempNoteManager;

public partial class CategoryDialog : Window
{
    private bool _isLoading = true;
    private bool _suppressColorEvents;

    public CategoryDialog()
        : this(null)
    {
    }

    public CategoryDialog(StorageCategory? category)
    {
        InitializeComponent();

        _isLoading = true;
        if (category is not null)
        {
            Title = "Редактировать категорию";
            OkButton.Content = "Сохранить";
            NameBox.Text = category.Name;
            DescriptionBox.Text = category.Description;
            AutoColorBox.IsChecked = false;
            SetColor(category.Color);
        }
        else
        {
            SetColor(CategoryColorService.SuggestColor(NameBox.Text, DescriptionBox.Text));
        }

        _isLoading = false;
        RefreshColorPreview();
    }

    public string CategoryName => NameBox.Text.Trim();

    public string Description => DescriptionBox.Text.Trim();

    public string Color
    {
        get
        {
            if (CategoryColorService.TryNormalizeHex(ColorHexBox.Text, out var normalized))
            {
                return normalized;
            }

            return "#2563EB";
        }
    }

    private void CategoryText_Changed(object sender, TextChangedEventArgs e)
    {
        if (_isLoading || AutoColorBox.IsChecked != true)
        {
            return;
        }

        SetColor(CategoryColorService.SuggestColor(CategoryName, Description));
    }

    private void ColorBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading || _suppressColorEvents)
        {
            return;
        }

        if (ColorBox.SelectedItem is ComboBoxItem item && item.Tag is not null)
        {
            AutoColorBox.IsChecked = false;
            SetColor(item.Tag.ToString() ?? "#2563EB");
        }
    }

    private void ColorHexBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isLoading || _suppressColorEvents)
        {
            return;
        }

        AutoColorBox.IsChecked = false;
        RefreshColorPreview();
    }

    private void AutoColor_Click(object sender, RoutedEventArgs e)
    {
        AutoColorBox.IsChecked = true;
        SetColor(CategoryColorService.SuggestColor(CategoryName, Description));
    }

    private void AutoColorMode_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoading || AutoColorBox.IsChecked != true)
        {
            return;
        }

        SetColor(CategoryColorService.SuggestColor(CategoryName, Description));
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(CategoryName))
        {
            WpfMessageBox.Show(this, "Введите название категории.", "Категория", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        foreach (var invalidChar in Path.GetInvalidFileNameChars())
        {
            if (CategoryName.Contains(invalidChar))
            {
                WpfMessageBox.Show(this, "Название содержит символы, которые нельзя использовать в имени папки.", "Категория", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }

        if (!CategoryColorService.TryNormalizeHex(ColorHexBox.Text, out _))
        {
            WpfMessageBox.Show(this, "Введите цвет в формате #RRGGBB или #RGB.", "Категория", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
    }

    private void SetColor(string color)
    {
        var normalized = CategoryColorService.TryNormalizeHex(color, out var parsed) ? parsed : "#2563EB";

        _suppressColorEvents = true;
        ColorHexBox.Text = normalized;
        SelectPreset(normalized);
        _suppressColorEvents = false;
        RefreshColorPreview();
    }

    private void SelectPreset(string color)
    {
        foreach (var item in ColorBox.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Tag?.ToString(), color, StringComparison.OrdinalIgnoreCase))
            {
                ColorBox.SelectedItem = item;
                return;
            }
        }

        ColorBox.SelectedIndex = -1;
    }

    private void RefreshColorPreview()
    {
        if (CategoryColorService.TryNormalizeHex(ColorHexBox.Text, out var normalized))
        {
            ColorPreview.Background = (WpfBrush)new BrushConverter().ConvertFromString(normalized)!;
            ColorHexBox.BorderBrush = WpfBrushes.LightGray;
            return;
        }

        ColorPreview.Background = WpfBrushes.Transparent;
        ColorHexBox.BorderBrush = WpfBrushes.IndianRed;
    }
}
