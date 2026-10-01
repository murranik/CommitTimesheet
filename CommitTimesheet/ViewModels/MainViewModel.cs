using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using CommitTimesheet.Models;
using CommitTimesheet.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CommitTimesheet.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private const string StatusSeparator = " · ";

    private readonly SettingsStore settingsStore;
    private readonly GitLogReader gitLogReader;
    private readonly WorkingStateReader workingStateReader;
    private readonly ClaudeSummarizer claudeSummarizer;
    private readonly TimesheetAssembler timesheetAssembler;
    private readonly TsvFormatter tsvFormatter;
    private readonly IDialogService dialogs;
    private readonly string? gitPath;

    private AppSettings settings;
    private CancellationTokenSource? loadCancellation;
    private CancellationTokenSource? generateCancellation;
    private bool isSettingRange;

    [ObservableProperty]
    private DateTime? fromDate;

    [ObservableProperty]
    private DateTime? toDate;

    [ObservableProperty]
    private string statusText;

    [ObservableProperty]
    private string commitsEmptyText;

    [ObservableProperty]
    private string commitCountText = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GenerateCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelGenerateCommand))]
    private bool isGenerating;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InsertRowCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteRowCommand))]
    private TimesheetRowViewModel? selectedTimesheetRow;

    public MainViewModel(
        SettingsStore settingsStore,
        GitLogReader gitLogReader,
        WorkingStateReader workingStateReader,
        ClaudeSummarizer claudeSummarizer,
        TimesheetAssembler timesheetAssembler,
        TsvFormatter tsvFormatter,
        IDialogService dialogs)
    {
        this.settingsStore = settingsStore;
        this.gitLogReader = gitLogReader;
        this.workingStateReader = workingStateReader;
        this.claudeSummarizer = claudeSummarizer;
        this.timesheetAssembler = timesheetAssembler;
        this.tsvFormatter = tsvFormatter;
        this.dialogs = dialogs;

        gitPath = ExecutableLocator.FindGit();
        var loaded = settingsStore.Load();
        settings = loaded.Settings;

        var (monthStart, monthEnd) = MonthOf(DateTime.Today);
        fromDate = monthStart;
        toDate = monthEnd;
        statusText = loaded.Notice ?? "";
        commitsEmptyText = gitPath is null ? "git not found" : "";

        TimesheetRows.CollectionChanged += OnTimesheetRowsChanged;
    }

    public ObservableCollection<CommitEntry> Commits { get; } = [];

    public ObservableCollection<TimesheetRowViewModel> TimesheetRows { get; } = [];

    partial void OnFromDateChanged(DateTime? value) => ReloadAfterRangeChange();

    partial void OnToDateChanged(DateTime? value) => ReloadAfterRangeChange();

    [RelayCommand]
    private void ThisMonth() => SetRange(MonthOf(DateTime.Today));

    [RelayCommand]
    private void LastMonth() => SetRange(MonthOf(DateTime.Today.AddMonths(-1)));

    [RelayCommand]
    private Task RefreshAsync() => LoadCommitsAsync();

    [RelayCommand(CanExecute = nameof(CanGenerate))]
    private async Task GenerateAsync()
    {
        if (gitPath is null || !TryGetRange(out var from, out var to))
        {
            StatusText = "Pick a valid date range first";
            return;
        }

        using var cancellation = new CancellationTokenSource();
        generateCancellation = cancellation;
        IsGenerating = true;
        try
        {
            var log = await gitLogReader.ReadAsync(gitPath, settings.Repos, settings.AuthorEmails, from, to, includeFiles: true, cancellation.Token);
            var today = DateOnly.FromDateTime(DateTime.Today);
            var rangeIncludesToday = from <= today && today <= to;
            var workingStates = rangeIncludesToday
                ? await workingStateReader.ReadAsync(gitPath, settings.Repos, cancellation.Token)
                : [];
            var hasUncommittedWork = workingStates.Any(state => state.ChangedFiles.Count > 0);

            IReadOnlyList<GeneratedEntry> entries = [];
            if (log.Commits.Count > 0 || hasUncommittedWork)
            {
                var claudePath = ExecutableLocator.ResolveClaude(settings.ClaudePath);
                if (claudePath is null)
                {
                    StatusText = "Claude CLI not found";
                    dialogs.ShowMessage(
                        "Claude CLI not found",
                        "The Claude Code CLI couldn't be found. Set its location in Settings → Claude CLI path.");
                    return;
                }

                var request = new SummaryRequest(
                    log.Commits,
                    workingStates,
                    rangeIncludesToday ? today : null,
                    settings.Repos.Select(RepoPath.NameOf).ToList(),
                    settings.HoursPerDay);
                entries = await claudeSummarizer.SummarizeAsync(claudePath, request, cancellation.Token);
            }

            var assembly = timesheetAssembler.Assemble(from, to, entries, settings.Who, settings.Project);
            ReplaceTimesheetRows(assembly.Rows);

            var status = new List<string>
            {
                log.Commits.Count == 0 && !hasUncommittedWork
                    ? "No commits in range, calendar rows only"
                    : $"Generated {assembly.Rows.Count} rows",
            };
            if (assembly.DroppedCount > 0)
            {
                status.Add($"Dropped {assembly.DroppedCount} AI rows dated outside the range or on weekends");
            }
            status.AddRange(log.Warnings);
            StatusText = string.Join(StatusSeparator, status);
        }
        catch (OperationCanceledException)
        {
            StatusText = "Generate cancelled";
        }
        catch (TimeoutException exception)
        {
            StatusText = $"Generate cancelled: {exception.Message}";
        }
        catch (ClaudeException exception)
        {
            StatusText = "Generate failed";
            dialogs.ShowError("Generate failed", exception.Message, exception.Details);
        }
        catch (Exception exception)
        {
            StatusText = "Generate failed";
            dialogs.ShowError("Generate failed", exception.Message, exception.ToString());
        }
        finally
        {
            generateCancellation = null;
            IsGenerating = false;
        }
    }

    private bool CanGenerate() => gitPath is not null && !IsGenerating;

    [RelayCommand(CanExecute = nameof(IsGenerating))]
    private void CancelGenerate() => generateCancellation?.Cancel();

    [RelayCommand(CanExecute = nameof(HasTimesheetRows))]
    private void CopyForSheet()
    {
        var invalidRow = TimesheetRows.FirstOrDefault(row => !row.HasValidHours);
        if (invalidRow is not null)
        {
            SelectedTimesheetRow = invalidRow;
            StatusText = $"Fix the hours on {invalidRow.DateText} before copying";
            return;
        }

        var text = tsvFormatter.Format(TimesheetRows.Select(row => row.ToModel()));
        StatusText = dialogs.TryCopyToClipboard(text)
            ? $"Copied {TimesheetRows.Count} rows"
            : "Couldn't open the clipboard, try again";
    }

    private bool HasTimesheetRows() => TimesheetRows.Count > 0;

    [RelayCommand(CanExecute = nameof(HasSelectedTimesheetRow))]
    private void InsertRow()
    {
        var selected = SelectedTimesheetRow!;
        var row = new TimesheetRowViewModel(new TimesheetRow(
            selected.Date,
            selected.Dow,
            string.IsNullOrEmpty(selected.Who) ? settings.Who : selected.Who,
            string.IsNullOrEmpty(selected.Project) ? settings.Project : selected.Project,
            null,
            ""));
        TimesheetRows.Insert(TimesheetRows.IndexOf(selected) + 1, row);
        SelectedTimesheetRow = row;
    }

    [RelayCommand(CanExecute = nameof(HasSelectedTimesheetRow))]
    private void DeleteRow()
    {
        var index = TimesheetRows.IndexOf(SelectedTimesheetRow!);
        TimesheetRows.RemoveAt(index);
        SelectedTimesheetRow = TimesheetRows.Count == 0 ? null : TimesheetRows[Math.Min(index, TimesheetRows.Count - 1)];
    }

    private bool HasSelectedTimesheetRow() => SelectedTimesheetRow is not null;

    [RelayCommand]
    private async Task OpenSettingsAsync()
    {
        var editor = new SettingsViewModel(settings, ExecutableLocator.FindClaude(), dialogs);
        if (!dialogs.EditSettings(editor))
        {
            return;
        }

        settings = editor.ToSettings();
        try
        {
            settingsStore.Save(settings);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            dialogs.ShowError("Couldn't save settings", exception.Message, null);
        }

        RecomputeDayChecks();
        await LoadCommitsAsync();
    }

    private async Task LoadCommitsAsync()
    {
        loadCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        loadCancellation = cancellation;

        if (gitPath is null)
        {
            CommitsEmptyText = "git not found";
            return;
        }

        if (!TryGetRange(out var from, out var to))
        {
            Commits.Clear();
            CommitCountText = "";
            CommitsEmptyText = "Pick a valid date range";
            return;
        }

        Commits.Clear();
        CommitCountText = "";
        CommitsEmptyText = "Loading commits…";
        try
        {
            var log = await gitLogReader.ReadAsync(gitPath, settings.Repos, settings.AuthorEmails, from, to, includeFiles: false, cancellation.Token);
            if (loadCancellation != cancellation)
            {
                return;
            }

            foreach (var commit in log.Commits)
            {
                Commits.Add(commit);
            }
            CommitCountText = log.Commits.Count == 1 ? "1 commit" : $"{log.Commits.Count} commits";
            CommitsEmptyText = log.Commits.Count == 0 ? "No commits in this range" : "";
            StatusText = string.Join(StatusSeparator, log.Warnings.Prepend($"Loaded {CommitCountText}"));
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            if (loadCancellation == cancellation)
            {
                CommitsEmptyText = "Couldn't load commits";
                StatusText = exception.Message;
            }
        }
    }

    private void ReloadAfterRangeChange()
    {
        if (!isSettingRange)
        {
            _ = LoadCommitsAsync();
        }
    }

    private void SetRange((DateTime From, DateTime To) range)
    {
        isSettingRange = true;
        try
        {
            FromDate = range.From;
            ToDate = range.To;
        }
        finally
        {
            isSettingRange = false;
        }
        _ = LoadCommitsAsync();
    }

    private bool TryGetRange(out DateOnly from, out DateOnly to)
    {
        from = FromDate is { } start ? DateOnly.FromDateTime(start) : default;
        to = ToDate is { } end ? DateOnly.FromDateTime(end) : default;
        return FromDate is not null && ToDate is not null && from <= to;
    }

    private static (DateTime From, DateTime To) MonthOf(DateTime day)
    {
        var start = new DateTime(day.Year, day.Month, 1);
        return (start, start.AddMonths(1).AddDays(-1));
    }

    private void ReplaceTimesheetRows(IEnumerable<TimesheetRow> rows)
    {
        foreach (var row in TimesheetRows)
        {
            row.PropertyChanged -= OnTimesheetRowPropertyChanged;
        }
        TimesheetRows.Clear();
        foreach (var row in rows)
        {
            TimesheetRows.Add(new TimesheetRowViewModel(row));
        }
    }

    private void OnTimesheetRowsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (var row in e.OldItems?.OfType<TimesheetRowViewModel>() ?? [])
        {
            row.PropertyChanged -= OnTimesheetRowPropertyChanged;
        }
        foreach (var row in e.NewItems?.OfType<TimesheetRowViewModel>() ?? [])
        {
            row.PropertyChanged += OnTimesheetRowPropertyChanged;
        }

        if (SelectedTimesheetRow is not null && !TimesheetRows.Contains(SelectedTimesheetRow))
        {
            SelectedTimesheetRow = null;
        }
        CopyForSheetCommand.NotifyCanExecuteChanged();
        RecomputeDayChecks();
    }

    private void OnTimesheetRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TimesheetRowViewModel.HoursText) or nameof(TimesheetRowViewModel.Title))
        {
            RecomputeDayChecks();
        }
    }

    private void RecomputeDayChecks()
    {
        foreach (var day in TimesheetRows.GroupBy(row => row.Date))
        {
            var rows = day.ToList();
            var check = DayCheckFor(day.Key, rows);
            for (var i = 0; i < rows.Count; i++)
            {
                rows[i].DayCheck = i == rows.Count - 1 ? check : "";
            }
        }
    }

    private string DayCheckFor(DateOnly date, IReadOnlyList<TimesheetRowViewModel> rows)
    {
        if (rows.Any(row => !row.HasValidHours))
        {
            return "⚠ invalid hours";
        }
        if (WorkCalendar.IsWeekend(date) || rows.All(row => row.IsBlank))
        {
            return "";
        }

        var total = rows.Sum(row => row.Hours ?? 0);
        return total == settings.HoursPerDay
            ? ""
            : $"⚠ day = {total.ToString("0.0###", CultureInfo.InvariantCulture)}";
    }
}
