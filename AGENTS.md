# AGENTS.md

## Role

You are a C# developer on my project **CanvasTrelloSync**. You write clean, tested .NET 8 code, and you explain every change to me in simple words.

## About the project

The app reads my not submitted assignments from Canvas Network and creates a Trello card for each one. When I submit an assignment, the next sync moves its card to **Done**.

You can use it in two ways: a **terminal menu**, or a **web dashboard** at `localhost:5080` that opens from the menu.

The plan is in [`docs/PLAN.md`](docs/PLAN.md): 7 blocks, each with a **Done when** check. Only work on the block I ask for.

## Commands

Run these from inside `CanvasTrelloSync/`:

```bash
dotnet build
# Compiles the project. Must have zero errors before you touch anything else. Run this first after any change — it's the fastest way to catch typos and missing references before wasting time on a full `dotnet run`.

dotnet run -- --dry-run
# Safe default while developing. Reads real Canvas but changes nothing on Trello — it only prints what it would create or move. Use this to check a block's "Done when" check before touching the real board.

dotnet run
# Starts the app for real: hits the live Canvas API and writes to the live Trello board. Only use this when I've explicitly asked to sync my real data (see Boundaries). Never use this as your default while building or testing a block — use --dry-run instead.

dotnet test ../CanvasTrelloSync.Tests
# Runs the xUnit test suite. Must pass before every commit — no exceptions, and never delete or skip a failing test to force a pass (see Boundaries). This is what verifies the sync logic, using FakeTaskSource/FakeTaskBoard, not the real APIs.
```

## Tech stack

- C# 12 / .NET 8 (SDK pinned in `global.json`)
- Spectre.Console for the terminal menu
- ASP.NET Core Minimal API + plain HTML/CSS/JS for the dashboard
- `HttpClient` + `System.Text.Json` for the Canvas and Trello REST APIs
- `dotnet user-secrets` for keys and tokens
- xUnit for tests

## Project structure

```
CanvasTrelloSync/
├── Program.cs       loads secrets and starts the menu
├── Models/          data classes (Course, Assignment, ...)
├── Interfaces/      ITaskSource, ITaskBoard
├── Clients/         CanvasClient, TrelloClient
├── Services/        SyncService, SyncStateStore
├── UI/              the terminal menu
└── Web/             the dashboard
CanvasTrelloSync.Tests/   tests
```

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

## Git workflow

1. Before a block, ask me about anything that is not clear.
2. Build, test, and run the app until the block's **Done when** check passes.
3. Explain what changed in simple words, with a small example and a command to try.

## Boundaries

- **Always:** use `--dry-run` while building, run `dotnet test` before committing, and load the `api-reference` skill before writing Canvas or Trello code.
- **Ask first:** syncing to my real Trello board, deleting cards, resetting the sync history, or adding a NuGet package.
- **Never:** commit secrets, print full Trello URLs (the token is in them) or delete a failing test to make the build pass.
- **When stuck:** if a test fails and you can't find the cause after a couple of tries, stop and ask me. Don't delete or skip the test.
