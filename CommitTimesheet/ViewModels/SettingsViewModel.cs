using System.Collections.ObjectModel;
using CommitTimesheet.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CommitTimesheet.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly IDialogService dialogs;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveRepoCommand))]
    private string? selectedRepo;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveEmailCommand))]
    private string? selectedEmail;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddEmailCommand))]
    private string newEmail = "";

    [ObservableProperty]
    private string who;

    [ObservableProperty]
    private string project;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    private decimal hoursPerDay;

    [ObservableProperty]
    private string claudePath;

    public SettingsViewModel(AppSettings settings, string? detectedClaudePath, IDialogService dialogs)
    {
        this.dialogs = dialogs;
        Repos = new ObservableCollection<string>(settings.Repos);
        AuthorEmails = new ObservableCollection<string>(settings.AuthorEmails);
        AuthorEmails.CollectionChanged += (_, _) => OnPropertyChanged(nameof(CanSave));
        who = settings.Who;
        project = settings.Project;
        hoursPerDay = settings.HoursPerDay;
        claudePath = settings.ClaudePath ?? "";
        ClaudePathHint = detectedClaudePath is null
            ? "Leave empty to auto-detect. Not found on PATH."
            : $"Leave empty to auto-detect ({detectedClaudePath}).";
    }

    public ObservableCollection<string> Repos { get; }

    public ObservableCollection<string> AuthorEmails { get; }

    public string ClaudePathHint { get; }

    public bool CanSave => HoursPerDay > 0 && AuthorEmails.Count > 0;

    public AppSettings ToSettings() => new()
    {
        Repos = Repos.ToList(),
        AuthorEmails = AuthorEmails.ToList(),
        Who = Who.Trim(),
        Project = Project.Trim(),
        HoursPerDay = HoursPerDay,
        ClaudePath = string.IsNullOrWhiteSpace(ClaudePath) ? null : ClaudePath.Trim(),
    };

    [RelayCommand]
    private void AddRepo()
    {
        var folder = dialogs.PickFolder();
        if (folder is not null && !Repos.Contains(folder, StringComparer.OrdinalIgnoreCase))
        {
            Repos.Add(folder);
        }
    }

    [RelayCommand(CanExecute = nameof(CanRemoveRepo))]
    private void RemoveRepo() => Repos.Remove(SelectedRepo!);

    private bool CanRemoveRepo() => SelectedRepo is not null;

    [RelayCommand(CanExecute = nameof(CanAddEmail))]
    private void AddEmail()
    {
        var email = NewEmail.Trim();
        if (!AuthorEmails.Contains(email, StringComparer.OrdinalIgnoreCase))
        {
            AuthorEmails.Add(email);
        }
        NewEmail = "";
    }

    private bool CanAddEmail() => !string.IsNullOrWhiteSpace(NewEmail);

    [RelayCommand(CanExecute = nameof(CanRemoveEmail))]
    private void RemoveEmail() => AuthorEmails.Remove(SelectedEmail!);

    private bool CanRemoveEmail() => SelectedEmail is not null;

    [RelayCommand]
    private void BrowseClaude()
    {
        var path = dialogs.PickExecutable(ClaudePath);
        if (path is not null)
        {
            ClaudePath = path;
        }
    }
}
