using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CommitTimesheet.ViewModels;

namespace CommitTimesheet.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void OnTimesheetGridPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete
            && e.OriginalSource is not TextBox
            && DataContext is MainViewModel viewModel
            && viewModel.DeleteRowCommand.CanExecute(null))
        {
            viewModel.DeleteRowCommand.Execute(null);
            e.Handled = true;
        }
    }
}
