using System.Globalization;
using CommitTimesheet.Models;

namespace CommitTimesheet.Services;

public sealed class TsvFormatter
{
    public string Format(IEnumerable<TimesheetRow> rows) =>
        string.Join("\r\n", rows.Select(FormatRow));

    public static string FormatHours(decimal? hours) =>
        hours?.ToString("0.####", CultureInfo.InvariantCulture) ?? "";

    private static string FormatRow(TimesheetRow row) =>
        string.Join('\t',
            WorkCalendar.SheetDate(row.Date),
            row.Dow,
            Clean(row.Who),
            Clean(row.Project),
            FormatHours(row.Hours),
            Clean(row.Title));

    private static string Clean(string value) =>
        value.Replace("\r\n", " ").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ').Trim();
}
