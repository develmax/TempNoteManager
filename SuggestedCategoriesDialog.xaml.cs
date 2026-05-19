using System.Collections.ObjectModel;
using System.Windows;
using TempNoteManager.Models;

namespace TempNoteManager;

public partial class SuggestedCategoriesDialog : Window
{
    public SuggestedCategoriesDialog(IEnumerable<SuggestedCategory> suggestions)
    {
        InitializeComponent();
        Suggestions = new ObservableCollection<SuggestedCategory>(suggestions);
        DataContext = this;
    }

    public ObservableCollection<SuggestedCategory> Suggestions { get; }

    public IReadOnlyList<SuggestedCategory> SelectedSuggestions =>
        Suggestions.Where(suggestion => suggestion.IsSelected).ToList();

    private void Create_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }
}
