# Commit Timesheet — Design

Status: design approved in brainstorming (Sep 30, 2026). Sections 7–8 (error handling, testing) were not walked through in that session — review them before planning.

## 1. Purpose

A small Windows desktop app that turns my git commits into rows for my monthly timesheet spreadsheet.

The spreadsheet columns are:

| Date | DOW | Who | Projects | Hrs | Ticket Title |
|---|---|---|---|---|---|
| 9/29/2026 | Tue | Nikita | MemberWork | 3 | Branch name on group-ex and class booking screens |

Today this is done by hand (or by asking Claude in a chat). The app should make it a two-click job: pick a range, click **Generate**, fix anything, click **Copy**, paste into the sheet.

### Success criteria

- The Commits table for a month loads in a few seconds, without AI.
- Generate for a full month produces rows that can be pasted into the sheet with only small edits.
- Nothing leaves the machine except the Generate prompt sent through the Claude Code CLI.

## 2. Decisions (locked)

| Topic | Decision |
|---|---|
| Stack | WPF, .NET 8, C#, MVVM via CommunityToolkit.Mvvm. No other NuGet dependencies unless required. |
| Location | `D:\Projects\CommitTimesheet`, separate from any client repo |
| Repos | Configurable list in settings. Defaults: `D:\Projects\MemberWorks\server-api`, `D:\Projects\MemberWorks\member-works`, `D:\Projects\MemberWorks\mw-mobile` |
| AI | Only when **Generate** is clicked. Runs the Claude Code CLI (`claude -p`) with the user's existing login. No API key. |
| Default view | Formatted table of raw commits, no AI |
| Output | Editable grid + **Copy** button (tab-separated rows to the clipboard). No saving to file in v1. |

## 3. Screens

### Main window

```
┌ Commit Timesheet ────────────────────────────────────────────────────────┐
│ From [ 9/1/2026 ▾]  To [ 9/30/2026 ▾]   [This month] [Last month]        │
│ [⟳ Refresh]  [✨ Generate]                                  [⚙ Settings] │
├─ Commits ──────────────────────────────────────────────────────────────── │
│ Date      DOW  Repo          Branch                   Message            │
│ 9/29/2026 Tue  mw-mobile     feature/branch_switcher  wip branch switcher│
│ 9/29/2026 Tue  server-api    feature/MW-1694…         feat: added branch…│
│ …                                                         112 commits    │
├─ Timesheet (after Generate) ───────────────────────────────────────────── │
│ Date      DOW  Who     Projects    Hrs  Ticket Title                     │
│ 9/29/2026 Tue  Nikita  MemberWork  3    Branch name on group-ex…         │
│ 9/29/2026 Tue  Nikita  MemberWork  3.5  Branch switcher (WIP)            │
│ 9/29/2026 Tue  Nikita  MemberWork  1.5  Build config fix…   ⚠ day = 8.0  │
│                                                   [📋 Copy for sheet]    │
└──────────────────────────────────────────────────────────────────────────┘
Status bar: "Generating with Claude… (Cancel)" / "Copied 72 rows"
```

- Date range defaults to the current month. **This month** / **Last month** are shortcuts.
- The Commits table reloads when the range changes or **Refresh** is clicked. Read-only. Sorted newest first. Footer shows the commit count.
- The Timesheet grid is empty until **Generate**. Generating again replaces it.
- Timesheet grid: **Hrs** and **Ticket Title** are editable, and so are **Who**/**Projects**. Rows can be added (insert below) and deleted.
- A day whose hours don't total the configured hours per day gets a ⚠ marker (with the actual total). Weekend rows and blank weekday rows are exempt. The app never rescales hours on its own.
- **Copy for sheet** copies every Timesheet row as tab-separated text, no header row.
- While Generate runs: Generate is disabled, the status bar shows progress with **Cancel**, and the Commits table stays usable.

### Settings window

| Field | Default |
|---|---|
| Repo folders (list, add via folder picker / remove) | the three MemberWorks repos |
| Author emails (list) | `nikita@member.works` |
| Who | `Nikita` |
| Project | `MemberWork` |
| Hours per day | `8` |
| Claude CLI path | auto-detected from `PATH` (e.g. `C:\Users\Murranik\.local\bin\claude.exe`), overridable |

Stored at `%APPDATA%\CommitTimesheet\settings.json`:

```json
{
  "repos": [
    "D:\\Projects\\MemberWorks\\server-api",
    "D:\\Projects\\MemberWorks\\member-works",
    "D:\\Projects\\MemberWorks\\mw-mobile"
  ],
  "authorEmails": ["nikita@member.works"],
  "who": "Nikita",
  "project": "MemberWork",
  "hoursPerDay": 8,
  "claudePath": null
}
```

## 4. Architecture

Single WPF project:

```
CommitTimesheet/
  Models/        CommitEntry, TimesheetRow, AppSettings, RepoWorkingState
  Services/
    GitLogReader        runs git log per repo → CommitEntry list
    WorkingStateReader  current branch + uncommitted files per repo (only when range includes today)
    SettingsStore       load/save settings.json
    ClaudeSummarizer    builds prompt, runs claude -p, parses JSON → AI rows
    TimesheetAssembler  merges AI rows with the calendar (weekend / empty-day rows)
    TsvFormatter        rows → tab-separated clipboard text
  ViewModels/    MainViewModel, SettingsViewModel
  Views/         MainWindow, SettingsWindow
```

Services talk only through the model types. Each one is a plain class so it can be swapped or tested in isolation. External tools (`git`, `claude`) are run through one small process-runner helper that captures stdout/stderr, supports cancellation, and has a timeout.

### Models

- `CommitEntry`: `Repo` (folder name), `Hash`, `AuthorDate` (local `DateTime`), `Branch` (nullable), `Subject`, `Files` (list of paths, filled in only for Generate).
- `RepoWorkingState`: `Repo`, `Branch`, `ChangedFiles`.
- `TimesheetRow`: `Date`, `Dow`, `Who`, `Project`, `Hours` (nullable decimal), `Title`.

## 5. Data flow

### Loading commits (no AI)

For each configured repo, in parallel:

```
git -C <repo> log --exclude=refs/stash --all --no-merges --source
    --since="<from> 00:00" --until="<to> 23:59:59"
    --author=<email1> --author=<email2> …
    --date=iso-strict --format=%H%x1f%ad%x1f%S%x1f%s
```

- Multiple `--author` flags are OR'd by git.
- `%S` (with `--source`) gives the ref the commit was reached from. Strip the `refs/heads/` / `refs/remotes/origin/` prefix to get the branch. Blank if missing.
- Drop stash commits: `--exclude=refs/stash` keeps the stash out of the walk, and subjects starting with `index on ` or `untracked files on ` are filtered as a backstop. (Dropping by `%S == refs/stash` is wrong: `--source` attributes real branch commits to the stash ref when the stash is walked first, which lost 19 of 81 mw-mobile commits in September 2026.)
- De-duplicate by hash within a repo.
- A repo folder that doesn't exist or isn't a git repo is skipped, with a warning in the status bar. It doesn't fail the whole load.

### Generate

1. Take the commits currently loaded for the range and fetch changed file paths in one call per repo: the same `git log` as above plus `--name-only`. Cap it at 15 paths per commit and add `(+N more)` when there are more.
2. If the range includes today, `WorkingStateReader` runs `git -C <repo> branch --show-current` and `git -C <repo> status --short` for each repo. Cap it at 30 paths per repo.
3. `ClaudeSummarizer` builds the prompt (section 6) and runs:
   ```
   claude -p --output-format json
   ```
   The prompt goes to stdin, and the working directory is a temp folder (so no project `CLAUDE.md` gets loaded). The timeout is 5 minutes, and Cancel kills the process.
4. Parse stdout as the CLI's JSON envelope and take the `result` string. Strip any ```` ``` ```` fences, then parse it as `[{ "date": "YYYY-MM-DD", "hours": number, "title": string }]`.
5. `TimesheetAssembler` goes through every day from `from` to `to`:
   - Saturday/Sunday → one row with only `Date` and `Dow`.
   - Weekday with AI rows → those rows, in the order they were returned, with `Who`/`Project` taken from settings.
   - Weekday without AI rows → one row with `Date`, `Dow`, `Who`, `Project` and blank hours/title.
   - AI rows dated outside the range, or on weekends, are dropped. The status bar says how many were dropped.

`Who`, `Project` and `DOW` always come from the app, never from the AI.

### Copy format

One line per row, tab-separated, `\r\n` line endings:

```
<M/d/yyyy>\t<ddd>\t<Who>\t<Project>\t<Hours>\t<Title>
```

- Date uses `M/d/yyyy` (e.g. `9/1/2026`) and DOW uses a short English name (`Tue`), with invariant culture for both.
- Hours are written as `8`, `3.5`, `0.5` (no trailing zeros), or empty.
- Weekend rows: `9/5/2026\tSat\t\t\t\t`.
- Titles have tabs and newlines replaced by spaces.

## 6. Generate prompt

The prompt template is embedded in the app. Placeholders are in `{braces}`.

```
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

Repos: server-api = backend (.NET), member-works = web (Angular), mw-mobile = mobile (Flutter).

Commits by date:
{for each date with commits}
## {yyyy-MM-dd} ({ddd})
- [{repo}] ({branch}) {subject}
    files: {path1}, {path2}, … (+N more)
{end}

{if range includes today}
Uncommitted today ({yyyy-MM-dd}):
- [{repo}] branch {branch}: {changedFile1}, {changedFile2}, …
{end}
```

The repo-to-surface line should come from settings eventually. In v1 it's generated from the repo folder names, with a hard-coded mapping for the three MemberWorks repos and no surface label for any other repo.

## 7. Error handling

| Situation | Behavior |
|---|---|
| `git` not on PATH | Commits table shows an empty state: "git not found". Generate disabled. |
| Repo path missing / not a git repo | Skip that repo; status bar warning naming it. |
| `claude` not found | Generate shows a message pointing to Settings → Claude CLI path. |
| `claude` exits non-zero, or the envelope has `is_error: true` | Show stderr / the error text in a dialog. Keep the previous Timesheet grid. |
| Result isn't valid JSON | Retry once with a follow-up prompt saying "Reply with only the JSON array." If it still fails, show the raw text in a dialog with a Copy button. |
| Timeout (5 min) / Cancel | Kill the process, status bar "Generate cancelled", keep previous grid. |
| No commits in range | Generate still works: it skips the AI call and returns calendar rows only. |
| Settings file corrupt | Back it up as `settings.json.bak` and start from defaults, with a status bar notice. |

## 8. Testing

Per my standing preference, v1 has no test project. The services are split so that tests are easy to add later, starting with `TimesheetAssembler`, `TsvFormatter` and the stash/branch parsing in `GitLogReader`.

Manual checks before calling v1 done:
- September 2026 with the three MemberWorks repos loads and shows around 100 commits.
- Generate for September 2026 produces weekend rows, blank rows for 9/1 and 9/10, and 8h totals on every commit day.
- The Copy output pastes cleanly into the spreadsheet (right columns, no extra blank columns).

## 9. Out of scope (v1)

- Saving and reopening generated timesheets.
- Exporting to xlsx, or writing directly to the spreadsheet or Google Sheets.
- Multiple profiles (different Who/Project per client). The settings shape allows adding this later.
- An installer. v1 is `dotnet publish` to a single self-contained `.exe`.
