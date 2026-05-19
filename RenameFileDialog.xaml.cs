using System.IO;
using System.Windows;
using WpfMessageBox = System.Windows.MessageBox;

namespace TempNoteManager;

public partial class RenameFileDialog : Window
{
    public RenameFileDialog(string currentName)
    {
        InitializeComponent();
        NameBox.Text = currentName;
        NameBox.SelectAll();
        NameBox.Focus();
    }

    public string NewFileName => NameBox.Text.Trim();

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NewFileName))
        {
            WpfMessageBox.Show(this, "Введите имя файла.", "Переименование", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        foreach (var invalidChar in Path.GetInvalidFileNameChars())
        {
            if (NewFileName.Contains(invalidChar))
            {
                WpfMessageBox.Show(this, "Имя содержит символы, которые нельзя использовать в имени файла.", "Переименование", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }

        DialogResult = true;
    }
}
