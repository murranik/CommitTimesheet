using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.Json;
using CommitTimesheet.Models;

namespace CommitTimesheet.Services;

public sealed record SummaryRequest(
    IReadOnlyList<CommitEntry> Commits,
    IReadOnlyList<RepoWorkingState> WorkingStates,
    DateOnly? Today,
    IReadOnlyList<string> RepoNames,
    decimal HoursPerDay);

public sealed class ClaudeException(string message, string details) : Exception(message)
{
    public string Details { get; } = details;
}

public sealed class ClaudeSummarizer(ProcessRunner runner)
{
    private const int MaxFilesPerCommit = 15;
    private const int MaxChangedFilesPerRepo = 30;
    private const string RetryPrompt = "Reply with only the JSON array.";
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(5);

    private static readonly Dictionary<string, string> KnownSurfaces = new(StringComparer.OrdinalIgnoreCase)
    {
        ["server-api"] = "backend (.NET)",
        ["member-works"] = "web (Angular)",
        ["mw-mobile"] = "mobile (Flutter)",
    };

    private const string Instructions = """
        You turn git commits into timesheet rows. Reply with ONLY a JSON array, no prose, no code fences.

        Each element: {"date": "YYYY-MM-DD", "hours": number, "title": string}.

        Rules:
        - Only produce rows for the dates listed below. Never invent dates.
        - For each date, split the work into subtasks: one row per distinct piece of work.
          Merge commits about the same feature across repos into one row
          (e.g. backend + web + mobile commits for "volunteer overhaul" → one row, or one row per surface if they are clearly separate chunks of work).
        - The hours for each date must add up to exactly {hoursPerDay}. Use multiples of 0.5. Give more hours to subtasks with more or bigger commits.
        - Collapse version-bump / release commits (e.g. "version: 1.0.1+34") into a single row like "Mobile release builds (1.0.1+34 – 1.0.1+36)", usually 0.5–1h.
        - Titles: short, human, past-tense or noun-phrase, like a ticket title. Mark unfinished work "(WIP)" when commits say wip.
          Mention the surface (backend / web / mobile) when it helps. No commit hashes, no conventional-commit prefixes like "feat:".
        - If a date has only uncommitted work (listed under "Uncommitted today"), infer rows from the branch names and changed files.
        """;

    private sealed record ClaudeReply(string Text, string? SessionId);

    public async Task<IReadOnlyList<GeneratedEntry>> SummarizeAsync(
        string claudePath,
        SummaryRequest request,
        CancellationToken cancellationToken)
    {
        var prompt = BuildPrompt(request);
        var reply = await RunClaudeAsync(claudePath, prompt, null, cancellationToken);
        if (TryParseEntries(reply.Text, out var entries))
        {
            return entries;
        }

        var retry = reply.SessionId is null
            ? await RunClaudeAsync(claudePath, prompt + "\n\n" + RetryPrompt, null, cancellationToken)
            : await RunClaudeAsync(claudePath, RetryPrompt, reply.SessionId, cancellationToken);
        if (TryParseEntries(retry.Text, out entries))
        {
            return entries;
        }

        throw new ClaudeException("Claude didn't return a valid JSON array, even after a retry.", retry.Text);
    }

    public static string BuildPrompt(SummaryRequest request)
    {
        var prompt = new StringBuilder();
        prompt.AppendLine(Instructions.Replace("{hoursPerDay}", TsvFormatter.FormatHours(request.HoursPerDay)));
        prompt.AppendLine();
        prompt.AppendLine(RepoLine(request.RepoNames));
        prompt.AppendLine();
        prompt.AppendLine("Commits by date:");

        var commitsByDate = request.Commits
            .GroupBy(commit => DateOnly.FromDateTime(commit.AuthorDate))
            .OrderBy(group => group.Key);
        foreach (var day in commitsByDate)
        {
            prompt.AppendLine($"## {WorkCalendar.IsoDate(day.Key)} ({WorkCalendar.ShortDayName(day.Key)})");
            foreach (var commit in day.OrderBy(commit => commit.AuthorDate))
            {
                var branch = string.IsNullOrEmpty(commit.Branch) ? "" : $"({commit.Branch}) ";
                prompt.AppendLine($"- [{commit.Repo}] {branch}{commit.Subject}");
                if (commit.Files.Count > 0)
                {
                    prompt.AppendLine($"    files: {CappedList(commit.Files, MaxFilesPerCommit)}");
                }
            }
        }

        var changedStates = request.WorkingStates.Where(state => state.ChangedFiles.Count > 0).ToList();
        if (request.Today is { } today && changedStates.Count > 0)
        {
            prompt.AppendLine();
            prompt.AppendLine($"Uncommitted today ({WorkCalendar.IsoDate(today)}):");
            foreach (var state in changedStates)
            {
                prompt.AppendLine($"- [{state.Repo}] branch {state.Branch}: {CappedList(state.ChangedFiles, MaxChangedFilesPerRepo)}");
            }
        }

        return prompt.ToString();
    }

    private static string RepoLine(IReadOnlyList<string> repoNames)
    {
        var described = repoNames.Select(name =>
            KnownSurfaces.TryGetValue(name, out var surface) ? $"{name} = {surface}" : name);
        return $"Repos: {string.Join(", ", described)}.";
    }

    private static string CappedList(IReadOnlyList<string> items, int max)
    {
        var shown = string.Join(", ", items.Take(max));
        return items.Count > max ? $"{shown} (+{items.Count - max} more)" : shown;
    }

    private async Task<ClaudeReply> RunClaudeAsync(
        string claudePath,
        string prompt,
        string? resumeSessionId,
        CancellationToken cancellationToken)
    {
        var workingDirectory = Path.Combine(Path.GetTempPath(), "CommitTimesheet");
        Directory.CreateDirectory(workingDirectory);

        List<string> arguments = ["-p", "--output-format", "json"];
        if (resumeSessionId is not null)
        {
            arguments.AddRange(["--resume", resumeSessionId]);
        }

        ProcessResult result;
        try
        {
            result = await runner.RunAsync(claudePath, arguments, workingDirectory, prompt, Timeout, cancellationToken);
        }
        catch (Win32Exception exception)
        {
            throw new ClaudeException($"Couldn't start the Claude CLI at {claudePath}.", exception.Message);
        }

        var envelope = ReadEnvelope(result.StandardOutput);
        if (envelope is not { } root)
        {
            var problem = result.ExitCode != 0
                ? $"The Claude CLI exited with code {result.ExitCode}."
                : "The Claude CLI returned output that isn't its JSON envelope.";
            throw new ClaudeException(problem, FirstNonEmpty(result.StandardError, result.StandardOutput));
        }

        var text = StringProperty(root, "result") ?? "";
        var isError = root.TryGetProperty("is_error", out var isErrorElement) && isErrorElement.ValueKind == JsonValueKind.True;
        if (isError || result.ExitCode != 0)
        {
            throw new ClaudeException("The Claude CLI reported an error.", FirstNonEmpty(text, result.StandardError, result.StandardOutput));
        }

        return new ClaudeReply(text, StringProperty(root, "session_id"));
    }

    private static JsonElement? ReadEnvelope(string output)
    {
        try
        {
            using var document = JsonDocument.Parse(output.Trim());
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object)
            {
                return root.Clone();
            }
            if (root.ValueKind == JsonValueKind.Array)
            {
                var resultMessage = root.EnumerateArray()
                    .LastOrDefault(element => element.ValueKind == JsonValueKind.Object && StringProperty(element, "type") == "result");
                return resultMessage.ValueKind == JsonValueKind.Object ? resultMessage.Clone() : null;
            }
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool TryParseEntries(string text, out IReadOnlyList<GeneratedEntry> entries)
    {
        entries = [];
        var start = text.IndexOf('[');
        var end = text.LastIndexOf(']');
        if (start < 0 || end < start)
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(text[start..(end + 1)]);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            var parsed = new List<GeneratedEntry>();
            foreach (var element in document.RootElement.EnumerateArray())
            {
                if (!TryParseEntry(element, out var entry))
                {
                    return false;
                }
                parsed.Add(entry);
            }
            entries = parsed;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryParseEntry(JsonElement element, out GeneratedEntry entry)
    {
        entry = null!;
        if (element.ValueKind != JsonValueKind.Object
            || StringProperty(element, "date") is not { } dateText
            || !DateOnly.TryParseExact(dateText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            || !element.TryGetProperty("hours", out var hoursElement)
            || !TryReadHours(hoursElement, out var hours)
            || StringProperty(element, "title") is not { } title)
        {
            return false;
        }

        entry = new GeneratedEntry(date, hours, title.Trim());
        return true;
    }

    private static bool TryReadHours(JsonElement element, out decimal hours)
    {
        hours = 0;
        return element.ValueKind switch
        {
            JsonValueKind.Number => element.TryGetDecimal(out hours),
            JsonValueKind.String => decimal.TryParse(element.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out hours),
            _ => false,
        };
    }

    private static string? StringProperty(JsonElement element, string name) =>
        element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static string FirstNonEmpty(params string[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? "(no output)";
}
