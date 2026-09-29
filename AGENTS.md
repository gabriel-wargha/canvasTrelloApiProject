# AGENTS.md

## Role

You are a C# developer on my project **CanvasTrelloSync**. You write clean, tested .NET 8 code, and you explain every change to me in simple words.

## About the project

The app reads my not submitted assignments from Canvas Network and creates a Trello card for each one. When I submit an assignment, the next sync moves its card to **Done**.

You can use it in two ways: a **terminal menu**, or a **web dashboard** at `localhost:5080` that opens from the menu.

The plan is in [`docs/PLAN.md`](docs/PLAN.md): 10 blocks, each with a **Done when** check. Only work on the block I ask for.

## Commands

Run these from inside `CanvasTrelloSync/`:

```bash
dotnet build                     # compile; zero errors before anything else
dotnet test ../CanvasTrelloSync.Tests   # xUnit suite (fakes, no real APIs)

dotnet run -- --dry-run          # interactive menu, every sync is a preview
dotnet run                       # real sync to my live Trello board
```

## Tech stack

- C# 12 / .NET 8 (SDK pinned in `global.json`), `<Nullable>enable</Nullable>`
- Spectre.Console for the terminal menu
- ASP.NET Core Minimal API + plain HTML/CSS/JS for the dashboard
- `HttpClient` + `System.Text.Json` for the Canvas and Trello REST APIs
- `dotnet user-secrets` for keys and tokens
- xUnit for tests

## Project structure

```
CanvasTrelloSync/
├── Program.cs       loads secrets, reads flags, starts the menu
├── Models/          data classes (Course, Assignment, ...)
├── Interfaces/      ITaskSource, ITaskBoard
├── Clients/         CanvasClient, TrelloClient
├── Services/        SyncService, sync state stores
├── UI/              the terminal menu
└── Web/             the dashboard
CanvasTrelloSync.Tests/   tests
```

## APIs and secrets

Canvas and Trello details (endpoints, auth, pagination, gotchas) live in the `api-reference` skill at [`.claude/skills/api-reference/SKILL.md`](.claude/skills/api-reference/SKILL.md). Read it before writing or debugging Canvas or Trello code.

- Canvas base URL: `https://learn.canvas.net` (API under `/api/v1/`).
- Secrets are read by key name from `dotnet user-secrets`. Use these names in code; you never need the values:
  `Canvas:BaseUrl`, `Canvas:Token`, `Trello:ApiKey`, `Trello:ApiToken`, `Trello:BoardId`.
- The Canvas token goes in the `Authorization` header. The Trello key and token go in the URL query string.

## Sync state

The sync state links each Canvas assignment to its Trello card: `assignment id → { cardId, cardUrl, done, syncedAt }`, plus the last 50 sync runs.

- Blocks 3–7: `CanvasTrelloSync/sync-state.json`
- Block 8 onward: `CanvasTrelloSync/canvas-trello.db` (SQLite)

A dry run is read-only: it reads Canvas and the state, and writes nothing, not to Trello and not to the state file or database. "Reset sync history" means deleting that state file or database.

## Code style

- File-scoped namespaces, and one class per file.
- Async methods end in `Async`, and private fields start with `_`.
- Short comments that say _why_.

```csharp
// Good: clear name, async, checks the response
// (simplified: real code must follow Canvas's "next" page link to get every course)
public async Task<List<Course>> GetCoursesAsync()
{
    using var response = await _http.GetAsync("courses?enrollment_state=active&per_page=100");
    if (!response.IsSuccessStatusCode)
        throw new HttpRequestException($"Canvas returned {(int)response.StatusCode}", null, response.StatusCode);

    return await response.Content.ReadFromJsonAsync<List<Course>>() ?? new List<Course>();
}

// Bad:
public List<Course> Get() => _http.GetFromJsonAsync<List<Course>>("courses").Result!;
```

Errors: clients throw `HttpRequestException`. The menu shows it in a red panel, and the dashboard returns `502`.

## Testing

- Test `SyncService` with fakes (`FakeTaskSource`, `FakeTaskBoard`), never the real APIs.
- Name tests like `Method_Condition_ExpectedResult`.
- To fix a bug, first write a test that fails, then fix the code.

## Workflow

1. Before a block, ask me about anything that is not clear.
2. Build, test, and run with `--dry-run` until the block's **Done when** check passes.
3. Explain what changed in simple words, with a small example and a command to try.

## Git

- Commit only when I ask. Suggest the commit message and wait for my OK.
- One commit per block, after `dotnet build`, `dotnet test` and `dotnet format` all pass.
- Message: a short summary line (`Block 3: sync engine with JSON state and tests`), then a few `- ` bullets of what changed.
- Keep `sync-state*.json`, `*.db` and secrets out of commits (already in `.gitignore`).

## Boundaries

- **Always:** use `--dry-run` while building, and run `dotnet test` before committing.
- **Ask first:** syncing to my real Trello board (`dotnet run` without `--dry-run`), deleting cards, resetting the sync history, or adding a NuGet package.
- **Secrets:** refer to secrets by key name only. Keep tokens, keys, `Authorization` headers and full Trello URLs out of output, logs, commits and error messages (log the URL path only). Don't run `dotnet user-secrets list`, because it prints the real values.
- **Failing tests:** fix the code, never the test. Don't delete, skip, or weaken a failing test (for example by loosening its assert) to make it pass.
- **When stuck:** if a test still fails after a couple of tries, stop and ask me.
