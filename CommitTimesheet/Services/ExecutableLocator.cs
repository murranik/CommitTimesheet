namespace CommitTimesheet.Services;

public static class ExecutableLocator
{
    private static readonly string[] Extensions = [".exe", ".cmd", ".bat"];

    public static string? FindGit() =>
        FindOnPath("git")
        ?? ExistingOrNull(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Git", "cmd", "git.exe"));

    public static string? FindClaude() =>
        FindOnPath("claude")
        ?? ExistingOrNull(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin", "claude.exe"));

    public static string? ResolveClaude(string? configuredPath) =>
        string.IsNullOrWhiteSpace(configuredPath) ? FindClaude() : ExistingOrNull(configuredPath.Trim());

    private static string? FindOnPath(string name)
    {
        var directories = (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var directory in directories)
        {
            foreach (var extension in Extensions)
            {
                var candidate = Path.Combine(directory.Trim('"'), name + extension);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }
        return null;
    }

    private static string? ExistingOrNull(string path) => File.Exists(path) ? path : null;
}
