using System.Collections.Generic;
using System.Windows;
using TempNoteManager.Models;

namespace TempNoteManager;

public partial class CategoryPickerDialog : Window
{
    public CategoryPickerDialog(IEnumerable<StorageCategory> categories)
    {
        InitializeComponent();
        CategoryBox.ItemsSource = categories;
        CategoryBox.SelectedIndex = 0;
    }

    public StorageCategory? SelectedCategory => CategoryBox.SelectedItem as StorageCategory;

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedCategory is null)
        {
            System.Windows.MessageBox.Show(this, "Выберите категорию.", "Категория", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        DialogResult = true;
    }
}
