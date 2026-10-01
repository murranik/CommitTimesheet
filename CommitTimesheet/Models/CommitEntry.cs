namespace CommitTimesheet.Models;

public sealed record CommitEntry(
    string Repo,
    string Hash,
    DateTime AuthorDate,
    string? Branch,
    string Subject,
    IReadOnlyList<string> Files);
