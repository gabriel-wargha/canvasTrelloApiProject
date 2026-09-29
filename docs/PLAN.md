# CanvasTrelloSync — Plan (v2)

Rebuilt 2026-09-28. The teacher allows full AI use: Claude writes and runs the code, and I review and understand every outcome.
Budget: **8 hours = 8 blocks of about 1 hour**.

---

## 1. The final product

One command, `dotnet run`, opens a **colorful terminal app**. From its menu I can also open a **web dashboard** in the browser. Both use the same sync engine, so a sync started in the terminal shows up on the web page too.

### What it does

1. Reads my **unsubmitted** Canvas Network assignments.
2. Creates a Trello card for each one in the **Later** list.
3. **Two-way sync:** when I submit an assignment on Canvas, the next sync moves its card to **Done**. The app creates the Done list itself if it is missing.
4. Remembers everything in `sync-state.json`, so it never creates duplicates and keeps a sync history.
5. `dotnet run -- --dry-run` makes the whole session safe: every sync is only a preview, and nothing changes on Trello.

### Terminal (Spectre.Console)

```
   ____                          _____          _ _
  / ___|__ _ _ ____   ____ _ ___|_   _| __ ___| | | ___
 | |   / _` | '_ \ \ / / _` / __| | || '__/ _ \ | |/ _ \
 | |__| (_| | | | \ V / (_| \__ \ | || | |  __/ | | (_) |
  \____\__,_|_| |_|\_/ \__,_|___/ |_||_|  \___|_|_|\___/   Sync

 Source: Canvas Network   Board: My Trello board   Last sync: 10:42

 What do you want to do?
 > 📚  List my courses
   📝  List assignments
   👀  Preview sync (dry run)
   🔄  Sync to Trello
   🌐  Open web dashboard
   🕒  Sync history
   🧹  Reset sync history
   🚪  Exit

 ╭─ Assignments ───────────────────────────────────────────────╮
 │ Status   Course      Assignment                    Card     │
 │ ✅ done  USF-PE-26   M1: AI Prompting Lab Part I   Done     │
 │ ⏳ todo  USF-PE-26   M2: Prompt Patterns           Later    │
 │ ⏳ todo  PD-0141     Reflection Journal            —        │
 ╰─────────────────────────────────────────────────────────────╯

 ⠋ Syncing...  ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━  14/20
 ╭─ Result ─────────────────────────────────────╮
 │ ✨ Created 3   ➜ Moved to Done 1   ⏭ Skipped 16 │
 ╰──────────────────────────────────────────────╯
```

### Web dashboard (http://localhost:5080)

```
┌──────────────────────────────────────────────────────────────────┐
│ CanvasTrelloSync                          [ 🔄 Sync now ]  ☾/☀   │
├──────────────────────────────────────────────────────────────────┤
│ PROGRESS PER COURSE                                              │
│ USF-PE-26  Prompt Engineering   ████████░░░░░░  6 / 10           │
│ PD-0141    Enhancing Learning   ███░░░░░░░░░░░  2 / 8            │
├───────────────────────────────┬──────────────────────────────────┤
│ TO-DO                         │ TRELLO BOARD                     │
│ ☐ M2: Prompt Patterns         │  Later            Done           │
│   Canvas ↗  Trello ↗          │ ┌──────────┐    ┌──────────┐     │
│ ☐ Reflection Journal          │ │M2 Prompt │    │M1 AI Lab │     │
│   Canvas ↗  Trello ↗          │ └──────────┘    └──────────┘     │
├───────────────────────────────┴──────────────────────────────────┤
│ SYNC HISTORY                                                     │
│ 10:42  terminal  +3 created  1 → Done                            │
│ 09:15  web       +0 created  0 → Done                            │
└──────────────────────────────────────────────────────────────────┘
```

---

## 2. Architecture

```
dotnet run [--dry-run]
   │
   ▼
Program.cs ── loads user-secrets, builds the objects below
   │
   ├── ITaskSource ◄── CanvasClient      (real Canvas Network)
   ├── ITaskBoard  ◄── TrelloClient
   ├── ISyncStateStore ◄── JsonSyncStateStore  (sync-state.json, Block 3)
   │                   ◄── SqliteSyncStateStore (canvas-trello.db, Block 8)
   ├── SyncService     (the engine: one sync at a time, locked)
   │      ▲        ▲
   │      │        │
   ├── Menu (Spectre)     DashboardServer (ASP.NET Minimal API, same process)
   │                           └── wwwroot/ index.html, app.js, styles.css
```

```
CanvasTrelloSync/
├── Program.cs
├── Models/        Course, Assignment, Submission, TrelloList, TrelloCard,
│                  SyncState, SyncedCard, SyncRun, CourseProgress
├── Interfaces/    ITaskSource, ITaskBoard
├── Clients/       CanvasClient, TrelloClient
├── Services/      ISyncStateStore, JsonSyncStateStore, SqliteSyncStateStore, SyncService
├── UI/            Menu
└── Web/           DashboardServer, wwwroot/
CanvasTrelloSync.Tests/   xUnit tests for SyncService (fake source + fake board)
```

### Sync rules (the heart of the app)

A new, not submitted assignment gets a card in Later. A submitted assignment whose card is still open gets moved to Done. Everything else is skipped.

The state file maps each `assignment id → { cardId, cardUrl, done, syncedAt }` and keeps the last 50 `SyncRun`s (time, source terminal/web, created, moved, skipped).

---

## 3. Course requirements

| Requirement | Where |
|---|---|
| Conditionals | sync rules, menu `switch`, secret checks, `--dry-run` flag |
| Loops | menu `while`, pagination `while`, `foreach` over courses/assignments |
| Functions | `SyncAsync`, `GetAllPagesAsync`, `CreateCardAsync`, `MoveCardAsync`, … |
| Classes | clients, services, models, `Menu`, `DashboardServer` |
| Data structures | `List<Assignment>`, `Dictionary<long, SyncedCard>`, `Dictionary<string,string>` (list name → id) |
| Stretch: interfaces | `ITaskSource`, `ITaskBoard` |
| Bonus: files | `JsonSyncStateStore` reads/writes JSON |
| Bonus: database | `SqliteSyncStateStore`: SQLite with `CREATE TABLE`, `INSERT`, `UPDATE`, `SELECT` |
| Bonus: tests | `CanvasTrelloSync.Tests` |

---

## 4. The 8 blocks

Each block: Claude asks questions first → writes code → builds and tests → **explains the outcome in simple English** → one git commit.
"Done when" is the check that must pass before the block ends.

### Block 1 — Foundation + Canvas (1h)
- Add packages: `Spectre.Console`, ASP.NET Core framework reference.
- Finish models (`TrelloList`, `TrelloCard`), interfaces `ITaskSource` / `ITaskBoard`.
- `CanvasClient` with Bearer auth, `include[]=submission`, and Link-header pagination.
- Read the `--dry-run` flag in `Program.cs`.
- Temporary `Program.cs` prints courses and assignments in a Spectre table.
- **Done when:** `dotnet run -- --dry-run` shows my real Canvas Network courses and their assignments with todo/done status.

### Block 2 — Trello client (1h)
- `TrelloClient`: get lists, **ensure the Done list exists**, create card (returns id + url), move card, get cards on a list.
- **Done when:** a throwaway test creates a test card in Later, moves it to Done, and deletes it, and I saw each step on the real board.

### Block 3 — Sync engine + tests (1h)
- `SyncState`, an `ISyncStateStore` interface, and `JsonSyncStateStore` (`sync-state.json`). The interface lets Block 8 swap JSON for SQLite without touching `SyncService`.
- `SyncService.SyncAsync(dryRun, source)` applying the sync rules, writing history, and locked so the terminal and web never sync at the same time.
- xUnit project with fake source/board. Tests: creates new, skips existing, moves submitted to Done, dry run changes nothing, second sync creates 0.
- **Done when:** `dotnet test` is all green.

### Block 4 — Beautiful terminal UI (1h)
- Spectre menu (arrow keys), header with FigletText, tables, spinner + progress bar during sync, result panel, history table, reset with confirmation, red error panels (bad token, missing list).
- When started with `--dry-run`, the header shows a yellow **DRY RUN** badge and "Sync to Trello" only previews.
- **Done when:** with `--dry-run`, every menu item works and looks clean, and the preview shows the cards it would create or move.

### Block 5 — Web API inside the app (1h)
- `DashboardServer` starts Kestrel on `localhost:5080` in the background, with quiet logs so the menu is not messed up.
- Endpoints: `GET /api/summary`, `/api/assignments`, `/api/board`, `/api/history`, `POST /api/sync`.
- Menu "Open web dashboard" starts the server once and opens the browser.
- **Done when:** with the menu still usable, `curl` on every endpoint returns correct JSON, and `POST /api/sync` adds a "web" row to history.

### Block 6 — Dashboard page (1h)
- `wwwroot/index.html` + `app.js` + `styles.css` (no build step): progress bars per course, to-do list with Canvas/Trello links, Later/Done board mirror, Sync-now button with spinner, history table, light/dark mode, works on a phone-width window.
- **Done when:** Claude opens the page in Chrome, clicks Sync now, and a screenshot shows all four sections updated.

### Block 7 — Real test, polish, README, demo (1h)
- Run the testing checklist against real Canvas Network + Trello (with my OK).
- Demo two-way sync: submit one real Canvas Network assignment, sync, and watch its card move to Done.
- `/code-review` and `/simplify` pass; fix findings.
- README section with screenshots, run steps, and APIs used; demo video script (3–4 min).
- **Done when:** checklist below is all ticked and the README is committed.

### Block 8 — SQLite database (1h)
Swap the JSON file for a small **SQLite** database: one file (`canvas-trello.db`), with no server to install.
- Add the `Microsoft.Data.Sqlite` package. Use plain SQL, with no ORM, so the SQL stays visible for learning.
- Two tables:
  ```sql
  CREATE TABLE synced_cards (
      assignment_id INTEGER PRIMARY KEY,
      card_id       TEXT NOT NULL,
      card_url      TEXT,
      done          INTEGER NOT NULL DEFAULT 0,   -- 0 = open, 1 = moved to Done
      synced_at     TEXT NOT NULL
  );
  CREATE TABLE sync_runs (
      id        INTEGER PRIMARY KEY AUTOINCREMENT,
      ran_at    TEXT NOT NULL,
      trigger   TEXT NOT NULL,                   -- 'terminal' or 'web'
      created   INTEGER NOT NULL,
      moved     INTEGER NOT NULL,
      skipped   INTEGER NOT NULL
  );
  ```
- `SqliteSyncStateStore` implements `ISyncStateStore`: it creates the tables on first run, and uses parameterized queries (`@id`) to avoid SQL injection.
- On first start, if `sync-state.json` exists, import it into the database once, so no card is duplicated.
- `Program.cs` switches to `SqliteSyncStateStore`. `SyncService`, the menu and the dashboard stay unchanged.
- Tests: run the same store tests against an in-memory SQLite database (`Data Source=:memory:`).
- Add `*.db` to `.gitignore`.
- **Done when:** `dotnet test` is green, a sync writes rows that `sqlite3 canvas-trello.db "SELECT * FROM synced_cards;"` shows, and a second sync still creates 0 cards.

### Testing checklist
- [ ] Courses and assignments list correctly
- [ ] Dry run (menu preview and `--dry-run`) creates nothing in Trello
- [ ] First sync creates cards; second sync creates **0**
- [ ] Submitting a real assignment → next sync moves card to Done
- [ ] Done list is created automatically when missing
- [ ] Bad token → red error panel, no crash
- [ ] Dashboard: all 4 sections, Sync now works, history shows "web"
- [ ] `dotnet test` green
- [ ] Block 8: rows appear in `synced_cards` and `sync_runs`; old JSON history was imported

---

## 5. Skills used

| Skill | Where it helps |
|---|---|
| `api-reference` (ours, `.claude/skills/`) | every block that calls Canvas or Trello |
| `mattpocock-skills:tdd` | Block 3: write the sync tests first |
| `run` | Blocks 1, 4, 5: launch the app and see it working |
| `claude-in-chrome` | Block 6: open, click, and screenshot the dashboard |
| `mattpocock-skills:diagnosing-bugs` | whenever something breaks |
| `code-review`, `simplify` | Block 7 (or end of any block) |
| `mattpocock-skills:grilling` | before a block, to test the plan with questions |
| `mattpocock-skills:writing-for-agents` | editing `AGENTS.md` or skills |

---

## 6. Optional extras (if time is left)
- Trello label per course (color by course)
- Short course labels (Canvas Network codes can be long)
- Choose which courses to sync
- Watch mode: auto-sync every X minutes while the dashboard is open
