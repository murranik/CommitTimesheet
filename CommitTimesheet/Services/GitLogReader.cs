using System.Globalization;
using CommitTimesheet.Models;

namespace CommitTimesheet.Services;

public sealed record GitLogResult(IReadOnlyList<CommitEntry> Commits, IReadOnlyList<string> Warnings);

public sealed class GitLogReader(ProcessRunner runner)
{
    private const char RecordSeparator = '\x1e';
    private const char FieldSeparator = '\x1f';
    private const string StashRef = "refs/stash";
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(1);
    private static readonly string[] StashSubjectPrefixes = ["index on ", "untracked files on "];
    private static readonly string[] BranchRefPrefixes = ["refs/heads/", "refs/tags/"];
    private const string RemoteRefPrefix = "refs/remotes/";

    private sealed record RepoLog(IReadOnlyList<CommitEntry> Commits, string? Warning);

    public async Task<GitLogResult> ReadAsync(
        string gitPath,
        IReadOnlyList<string> repoPaths,
        IReadOnlyList<string> authorEmails,
        DateOnly from,
        DateOnly to,
        bool includeFiles,
        CancellationToken cancellationToken)
    {
        if (authorEmails.Count == 0)
        {
            return new GitLogResult([], ["No author emails configured in Settings"]);
        }

        var logs = await Task.WhenAll(repoPaths.Select(repoPath =>
            ReadRepoAsync(gitPath, repoPath, authorEmails, from, to, includeFiles, cancellationToken)));

        var commits = logs
            .SelectMany(log => log.Commits)
            .OrderByDescending(commit => commit.AuthorDate)
            .ToList();
        var warnings = logs
            .Select(log => log.Warning)
            .OfType<string>()
            .ToList();
        return new GitLogResult(commits, warnings);
    }

    private async Task<RepoLog> ReadRepoAsync(
        string gitPath,
        string repoPath,
        IReadOnlyList<string> authorEmails,
        DateOnly from,
        DateOnly to,
        bool includeFiles,
        CancellationToken cancellationToken)
    {
        var repo = RepoPath.NameOf(repoPath);
        if (!Directory.Exists(repoPath))
        {
            return new RepoLog([], $"Skipped {repo}: folder not found");
        }

        var result = await runner.RunAsync(
            gitPath,
            BuildArguments(repoPath, authorEmails, from, to, includeFiles),
            repoPath,
            null,
            Timeout,
            cancellationToken);

        if (result.ExitCode != 0)
        {
            return new RepoLog([], $"Skipped {repo}: {FirstLine(result.StandardError)}");
        }

        var commits = ParseLog(repo, result.StandardOutput)
            .Where(commit => !IsStash(commit))
            .Where(commit => DateOnly.FromDateTime(commit.AuthorDate) >= from && DateOnly.FromDateTime(commit.AuthorDate) <= to)
            .DistinctBy(commit => commit.Hash)
            .ToList();
        return new RepoLog(commits, null);
    }

    private static IEnumerable<string> BuildArguments(
        string repoPath,
        IReadOnlyList<string> authorEmails,
        DateOnly from,
        DateOnly to,
        bool includeFiles)
    {
        yield return "-c";
        yield return "core.quotepath=off";
        yield return "-c";
        yield return "log.showSignature=false";
        yield return "-C";
        yield return repoPath;
        yield return "log";
        yield return $"--exclude={StashRef}";
        yield return "--all";
        yield return "--no-merges";
        yield return "--source";
        yield return $"--since={WorkCalendar.IsoDate(from)} 00:00";
        yield return $"--until={WorkCalendar.IsoDate(to)} 23:59:59";
        foreach (var email in authorEmails)
        {
            yield return $"--author={email}";
        }
        yield return "--date=iso-strict";
        yield return "--format=%x1e%H%x1f%ad%x1f%S%x1f%s";
        if (includeFiles)
        {
            yield return "--name-only";
        }
    }

    private static IEnumerable<CommitEntry> ParseLog(string repo, string output)
    {
        foreach (var record in output.Split(RecordSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var lines = record.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (lines.Length == 0)
            {
                continue;
            }

            var fields = lines[0].Split(FieldSeparator, 4);
            if (fields.Length < 4 || !DateTimeOffset.TryParse(fields[1], CultureInfo.InvariantCulture, DateTimeStyles.None, out var authorDate))
            {
                continue;
            }

            yield return new CommitEntry(
                repo,
                fields[0],
                authorDate.LocalDateTime,
                BranchFromSource(fields[2]),
                fields[3],
                lines.Skip(1).ToList());
        }
    }

    private static bool IsStash(CommitEntry commit) =>
        StashSubjectPrefixes.Any(prefix => commit.Subject.StartsWith(prefix, StringComparison.Ordinal))
        || commit.Branch == StashRef;

    private static string? BranchFromSource(string source)
    {
        if (string.IsNullOrWhiteSpace(source) || source == "HEAD")
        {
            return null;
        }

        foreach (var prefix in BranchRefPrefixes)
        {
            if (source.StartsWith(prefix, StringComparison.Ordinal))
            {
                return source[prefix.Length..];
            }
        }

        if (source.StartsWith(RemoteRefPrefix, StringComparison.Ordinal))
        {
            var remoteAndBranch = source[RemoteRefPrefix.Length..];
            var slash = remoteAndBranch.IndexOf('/');
            return slash < 0 ? remoteAndBranch : remoteAndBranch[(slash + 1)..];
        }

        return source;
    }

    private static string FirstLine(string text)
    {
        var line = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        return string.IsNullOrEmpty(line) ? "git failed" : line;
    }
}
