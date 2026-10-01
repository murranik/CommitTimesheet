namespace CommitTimesheet.Models;

public sealed record AppSettings
{
    public IReadOnlyList<string> Repos { get; init; } =
    [
        @"D:\Projects\MemberWorks\server-api",
        @"D:\Projects\MemberWorks\member-works",
        @"D:\Projects\MemberWorks\mw-mobile",
    ];

    public IReadOnlyList<string> AuthorEmails { get; init; } = ["nikita@member.works"];

    public string Who { get; init; } = "Nikita";

    public string Project { get; init; } = "MemberWork";

    public decimal HoursPerDay { get; init; } = 8;

    public string? ClaudePath { get; init; }
}
