using System.Windows;

namespace CommitTimesheet.Views;

public partial class ErrorWindow : Window
{
    public ErrorWindow(string title, string message, string details)
    {
        InitializeComponent();
        Title = title;
        MessageText.Text = message;
        DetailsText.Text = details;
    }

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        CopyLabel.Text = DialogService.SetClipboardText(DetailsText.Text) ? "Copied" : "Copy failed";
    }
}
