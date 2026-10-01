namespace CommitTimesheet.Services;

public static class RepoPath
{
    public static string NameOf(string repoPath) =>
        Path.GetFileName(repoPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
}
