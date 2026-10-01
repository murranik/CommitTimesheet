using System.Globalization;
using CommitTimesheet.Models;
using CommitTimesheet.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CommitTimesheet.ViewModels;

public partial class TimesheetRowViewModel : ObservableObject
{
    [ObservableProperty]
    private string who;

    [ObservableProperty]
    private string project;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Hours))]
    [NotifyPropertyChangedFor(nameof(HasValidHours))]
    private string hoursText;

    [ObservableProperty]
    private string title;

    [ObservableProperty]
    private string dayCheck = "";

    public TimesheetRowViewModel(TimesheetRow row)
    {
        Date = row.Date;
        Dow = row.Dow;
        who = row.Who;
        project = row.Project;
        hoursText = TsvFormatter.FormatHours(row.Hours);
        title = row.Title;
    }

    public DateOnly Date { get; }

    public string Dow { get; }

    public string DateText => WorkCalendar.SheetDate(Date);

    public bool IsWeekend => WorkCalendar.IsWeekend(Date);

    public decimal? Hours => TryParseHours(HoursText, out var hours) ? hours : null;

    public bool HasValidHours => TryParseHours(HoursText, out _);

    public bool IsBlank => string.IsNullOrWhiteSpace(HoursText) && string.IsNullOrWhiteSpace(Title);

    public TimesheetRow ToModel() => new(Date, Dow, Who, Project, Hours, Title);

    private static bool TryParseHours(string text, out decimal? hours)
    {
        hours = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        if (decimal.TryParse(text.Trim().Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) && parsed >= 0)
        {
            hours = parsed;
            return true;
        }
        return false;
    }
}
