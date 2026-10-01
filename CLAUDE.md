# CommitTimesheet

A WPF (.NET 8) desktop app that reads my git commits from configured repos and, on demand, uses the Claude Code CLI to turn them into timesheet rows I paste into a spreadsheet.

The design lives in [docs/superpowers/specs/2026-09-30-commit-timesheet-design.md](docs/superpowers/specs/2026-09-30-commit-timesheet-design.md). Read it before doing anything.

## Where things stand

- v1 is implemented in `CommitTimesheet.sln` / `CommitTimesheet/` (built straight from the spec, no separate plan document).
- Verified against September 2026: 107 commits load in about 0.1s, and Generate through the Claude CLI gives 8h on every commit day plus blank rows for 9/1 and 9/10.
- Publish a single exe with `dotnet publish CommitTimesheet/CommitTimesheet.csproj -p:PublishProfile=SingleFile` (output goes to `CommitTimesheet/bin/publish/`).

## Working rules

- **I manage all git operations.** Don't run git commands against this repo, don't commit, and don't suggest committing. (The app itself running `git log` against the configured repos at runtime is fine.)
- **No code comments.** Code should explain itself through names and structure.
- **Skip tests unless I ask.** Write production code only, no test project in v1.
- Keep it to one WPF project (`CommitTimesheet`) with the Models / Services / ViewModels / Views layout from the spec. CommunityToolkit.Mvvm is the only planned NuGet dependency.
- Target `net8.0-windows`. The SDKs installed are 8.0.404 and 9.0.x.
- Local tool paths: `claude` at `C:\Users\Murranik\.local\bin\claude.exe`, `git` at `C:\Program Files\Git\cmd\git.exe`.

## Reference: the manual process this app replaces

This is how the September 2026 timesheet was built by hand. The app's Generate output should look like this.

- Repos: `D:\Projects\MemberWorks\server-api`, `member-works`, `mw-mobile`. Author email is `nikita@member.works`; the author name appears as both "Voloshchuk" and "Voloshchuck", so filter by email.
- Drop merge commits and stash commits ("index on …", "untracked files on …").
- Every day of the month gets a row:
  - weekends show only the date and day of week
  - weekdays with no commits show `Nikita`, `MemberWork` and blank hours/title
- Each commit day is split into subtask rows that total 8h in half-hour steps. Related backend, web and mobile commits merge into one row, and version bumps collapse into one "Mobile release builds (x – y)" row.
- If today is in the range and has no commits yet, its rows come from each repo's current branch and uncommitted changes.

Sample output (tab-separated):

```
9/4/2026	Fri	Nikita	MemberWork	3	Mobile branch-level build targets (env/staging, env/production)
9/4/2026	Fri	Nikita	MemberWork	1.5	Pre-push hook guarding build target
9/4/2026	Fri	Nikita	MemberWork	2	Dev and staging build config updates
9/4/2026	Fri	Nikita	MemberWork	0.5	iOS launch icons
9/4/2026	Fri	Nikita	MemberWork	1	Mobile release builds (1.0.0+24 – 1.0.0+26)
9/5/2026	Sat				
9/10/2026	Thu	Nikita	MemberWork		
```
