using System.Text.Json;
using CommitTimesheet.Models;

namespace CommitTimesheet.Services;

public sealed record SettingsLoadResult(AppSettings Settings, string? Notice);

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly string filePath;

    public SettingsStore()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "CommitTimesheet",
            "settings.json"))
    {
    }

    public SettingsStore(string filePath)
    {
        this.filePath = filePath;
    }

    public SettingsLoadResult Load()
    {
        if (!File.Exists(filePath))
        {
            return new SettingsLoadResult(new AppSettings(), null);
        }

        try
        {
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(filePath), JsonOptions)
                ?? throw new JsonException("Settings file is empty.");
            return new SettingsLoadResult(WithoutNulls(settings), null);
        }
        catch (JsonException)
        {
            var backupPath = filePath + ".bak";
            File.Move(filePath, backupPath, overwrite: true);
            return new SettingsLoadResult(
                new AppSettings(),
                $"Settings file was corrupt. It was backed up to {Path.GetFileName(backupPath)} and defaults are in use.");
        }
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        File.WriteAllText(filePath, JsonSerializer.Serialize(settings, JsonOptions));
    }

    private static AppSettings WithoutNulls(AppSettings settings) => settings with
    {
        Repos = settings.Repos ?? [],
        AuthorEmails = settings.AuthorEmails ?? [],
        Who = settings.Who ?? "",
        Project = settings.Project ?? "",
    };
}
