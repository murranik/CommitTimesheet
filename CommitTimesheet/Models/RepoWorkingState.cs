namespace CommitTimesheet.Models;

public sealed record RepoWorkingState(string Repo, string Branch, IReadOnlyList<string> ChangedFiles);
