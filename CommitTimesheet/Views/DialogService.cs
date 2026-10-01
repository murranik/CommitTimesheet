using System.Runtime.InteropServices;
using System.Windows;
using CommitTimesheet.ViewModels;
using Microsoft.Win32;

namespace CommitTimesheet.Views;

public sealed class DialogService : IDialogService
{
    public string? PickFolder()
    {
        var dialog = new OpenFolderDialog { Title = "Choose a git repository folder" };
        return dialog.ShowDialog(ActiveWindow()) == true ? dialog.FolderName : null;
    }

    public string? PickExecutable(string? currentPath)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Locate the Claude Code CLI",
            Filter = "Programs (*.exe;*.cmd;*.bat)|*.exe;*.cmd;*.bat|All files (*.*)|*.*",
        };
        if (!string.IsNullOrWhiteSpace(currentPath) && Path.GetDirectoryName(currentPath) is { } directory && Directory.Exists(directory))
        {
            dialog.InitialDirectory = directory;
        }
        return dialog.ShowDialog(ActiveWindow()) == true ? dialog.FileName : null;
    }

    public bool EditSettings(SettingsViewModel settings)
    {
        var window = new SettingsWindow { DataContext = settings, Owner = ActiveWindow() };
        return window.ShowDialog() == true;
    }

    public void ShowMessage(string title, string message) =>
        ShowMessageBox(title, message, MessageBoxImage.Information);

    public void ShowError(string title, string message, string? details)
    {
        if (string.IsNullOrWhiteSpace(details))
        {
            ShowMessageBox(title, message, MessageBoxImage.Error);
            return;
        }

        var window = new ErrorWindow(title, message, details);
        if (ActiveWindow() is { IsVisible: true } owner)
        {
            window.Owner = owner;
        }
        window.ShowDialog();
    }

    public bool TryCopyToClipboard(string text) => SetClipboardText(text);

    public static bool SetClipboardText(string text)
    {
        try
        {
            Clipboard.SetDataObject(text, copy: true);
            return true;
        }
        catch (ExternalException)
        {
            return false;
        }
    }

    private static void ShowMessageBox(string title, string message, MessageBoxImage image)
    {
        if (ActiveWindow() is { IsVisible: true } owner)
        {
            MessageBox.Show(owner, message, title, MessageBoxButton.OK, image);
        }
        else
        {
            MessageBox.Show(message, title, MessageBoxButton.OK, image);
        }
    }

    private static Window? ActiveWindow() =>
        Application.Current?.Windows.OfType<Window>().FirstOrDefault(window => window.IsActive)
        ?? Application.Current?.MainWindow;
}
