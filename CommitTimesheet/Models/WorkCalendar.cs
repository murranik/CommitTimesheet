using System.Globalization;

namespace CommitTimesheet.Models;

public static class WorkCalendar
{
    public static bool IsWeekend(DateOnly date) =>
        date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

    public static string ShortDayName(DateOnly date) =>
        date.ToString("ddd", CultureInfo.InvariantCulture);

    public static string SheetDate(DateOnly date) =>
        date.ToString("M/d/yyyy", CultureInfo.InvariantCulture);

    public static string IsoDate(DateOnly date) =>
        date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static IEnumerable<DateOnly> Days(DateOnly from, DateOnly to)
    {
        for (var day = from; day <= to; day = day.AddDays(1))
        {
            yield return day;
        }
    }
}
