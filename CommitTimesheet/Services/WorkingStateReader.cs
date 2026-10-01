using CommitTimesheet.Models;

namespace CommitTimesheet.Services;

public sealed class WorkingStateReader(ProcessRunner runner)
{
    private const string DetachedBranch = "(detached)";
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(1);

    public async Task<IReadOnlyList<RepoWorkingState>> ReadAsync(
        string gitPath,
        IReadOnlyList<string> repoPaths,
        CancellationToken cancellationToken)
    {
        var states = await Task.WhenAll(repoPaths.Select(repoPath => ReadRepoAsync(gitPath, repoPath, cancellationToken)));
        return states.OfType<RepoWorkingState>().ToList();
    }

    private async Task<RepoWorkingState?> ReadRepoAsync(string gitPath, string repoPath, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(repoPath))
        {
            return null;
        }

        var branch = await RunGitAsync(gitPath, repoPath, ["branch", "--show-current"], cancellationToken);
        var status = await RunGitAsync(gitPath, repoPath, ["--no-optional-locks", "status", "--short"], cancellationToken);
        if (branch is null || status is null)
        {
            return null;
        }

        var changedFiles = status
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.TrimEnd('\r'))
            .Where(line => line.Length > 3)
            .Select(line => line[3..].Trim())
            .ToList();

        var branchName = branch.Trim();
        return new RepoWorkingState(
            RepoPath.NameOf(repoPath),
            branchName.Length == 0 ? DetachedBranch : branchName,
            changedFiles);
    }

    private async Task<string?> RunGitAsync(
        string gitPath,
        string repoPath,
        IEnumerable<string> command,
        CancellationToken cancellationToken)
    {
        string[] arguments = ["-c", "core.quotepath=off", "-C", repoPath, .. command];
        var result = await runner.RunAsync(gitPath, arguments, repoPath, null, Timeout, cancellationToken);
        return result.ExitCode == 0 ? result.StandardOutput : null;
    }
}
