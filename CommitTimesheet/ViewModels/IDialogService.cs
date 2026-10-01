namespace CommitTimesheet.ViewModels;

public interface IDialogService
{
    string? PickFolder();

    string? PickExecutable(string? currentPath);

    bool EditSettings(SettingsViewModel settings);

    void ShowMessage(string title, string message);

    void ShowError(string title, string message, string? details);

    bool TryCopyToClipboard(string text);
}
