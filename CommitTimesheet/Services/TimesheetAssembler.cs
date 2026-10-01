using CommitTimesheet.Models;

namespace CommitTimesheet.Services;

public sealed record TimesheetAssembly(IReadOnlyList<TimesheetRow> Rows, int DroppedCount);

public sealed class TimesheetAssembler
{
    public TimesheetAssembly Assemble(
        DateOnly from,
        DateOnly to,
        IReadOnlyList<GeneratedEntry> entries,
        string who,
        string project)
    {
        var entriesByDate = entries
            .Where(entry => entry.Date >= from && entry.Date <= to && !WorkCalendar.IsWeekend(entry.Date))
            .GroupBy(entry => entry.Date)
            .ToDictionary(group => group.Key, group => group.ToList());
        var droppedCount = entries.Count - entriesByDate.Values.Sum(list => list.Count);

        var rows = new List<TimesheetRow>();
        foreach (var day in WorkCalendar.Days(from, to))
        {
            var dow = WorkCalendar.ShortDayName(day);
            if (WorkCalendar.IsWeekend(day))
            {
                rows.Add(new TimesheetRow(day, dow, "", "", null, ""));
            }
            else if (entriesByDate.TryGetValue(day, out var dayEntries))
            {
                rows.AddRange(dayEntries.Select(entry => new TimesheetRow(day, dow, who, project, entry.Hours, entry.Title)));
            }
            else
            {
                rows.Add(new TimesheetRow(day, dow, who, project, null, ""));
            }
        }

        return new TimesheetAssembly(rows, droppedCount);
    }
}
