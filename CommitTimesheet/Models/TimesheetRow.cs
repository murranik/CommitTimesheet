namespace CommitTimesheet.Models;

public sealed record TimesheetRow(
    DateOnly Date,
    string Dow,
    string Who,
    string Project,
    decimal? Hours,
    string Title);
