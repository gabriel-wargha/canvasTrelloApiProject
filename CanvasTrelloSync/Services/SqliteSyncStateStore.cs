using System.Globalization;
using CanvasTrelloSync.Models;
using Microsoft.Data.Sqlite;

namespace CanvasTrelloSync.Services;

// Keeps the sync state in a SQLite database (canvas-trello.db) with plain SQL, no ORM.
// Every value goes in through a parameter (@id, @card_id, ...), never pasted into the SQL text, so no SQL injection.
public class SqliteSyncStateStore : ISyncStateStore, IDisposable
{
    private const string CreateTablesSql = """
        CREATE TABLE IF NOT EXISTS synced_cards (
            assignment_id INTEGER PRIMARY KEY,
            card_id       TEXT NOT NULL,
            card_url      TEXT,
            done          INTEGER NOT NULL DEFAULT 0,   -- 0 = open, 1 = moved to Done
            synced_at     TEXT NOT NULL
        );
        CREATE TABLE IF NOT EXISTS sync_runs (
            id        INTEGER PRIMARY KEY AUTOINCREMENT,
            ran_at    TEXT NOT NULL,
            trigger   TEXT NOT NULL,                   -- 'terminal' or 'web'
            created   INTEGER NOT NULL,
            moved     INTEGER NOT NULL,
            skipped   INTEGER NOT NULL
        );
        """;

    // One connection for the store's whole life: an in-memory database (tests) disappears when its connection closes
    private readonly SqliteConnection _connection;

    // A connection can't run two commands at once, and the menu and dashboard may read while a sync saves
    private readonly SemaphoreSlim _lock = new(1, 1);

    public SqliteSyncStateStore(string connectionString)
    {
        _connection = new SqliteConnection(connectionString);
        _connection.Open();

        // First run: make the tables. Later runs: IF NOT EXISTS makes this do nothing.
        using var command = _connection.CreateCommand();
        command.CommandText = CreateTablesSql;
        command.ExecuteNonQuery();
    }

    public async Task<SyncState> LoadAsync()
    {
        await _lock.WaitAsync();
        try
        {
            var state = new SyncState();

            using (var command = _connection.CreateCommand())
            {
                command.CommandText = "SELECT assignment_id, card_id, card_url, done, synced_at FROM synced_cards";
                using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    state.Cards[reader.GetInt64(0)] = new SyncedCard
                    {
                        CardId = reader.GetString(1),
                        CardUrl = reader.IsDBNull(2) ? null : reader.GetString(2),
                        Done = reader.GetInt64(3) == 1,
                        SyncedAt = ParseDate(reader.GetString(4)),
                    };
                }
            }

            using (var command = _connection.CreateCommand())
            {
                // The newest MaxHistory runs, turned back to oldest first
                command.CommandText = """
                    SELECT ran_at, trigger, created, moved, skipped FROM (
                        SELECT * FROM sync_runs ORDER BY id DESC LIMIT @max
                    ) ORDER BY id
                    """;
                command.Parameters.AddWithValue("@max", SyncState.MaxHistory);
                using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    state.History.Add(new SyncRun
                    {
                        RanAt = ParseDate(reader.GetString(0)),
                        Trigger = reader.GetString(1),
                        Created = reader.GetInt32(2),
                        Moved = reader.GetInt32(3),
                        Skipped = reader.GetInt32(4),
                    });
                }
            }

            return state;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SaveAsync(SyncState state)
    {
        await _lock.WaitAsync();
        try
        {
            // One transaction: either every row is saved, or (on a crash) none are
            using var transaction = _connection.BeginTransaction();

            foreach (var (assignmentId, card) in state.Cards)
                await SaveCardAsync(transaction, assignmentId, card);

            // History is append-only: insert just the runs newer than the newest one already stored
            DateTimeOffset? newestStored = await GetNewestRunTimeAsync(transaction);
            foreach (var run in state.History.Where(run => newestStored is null || run.RanAt > newestStored))
                await InsertRunAsync(transaction, run);

            await TrimHistoryAsync(transaction);
            transaction.Commit();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task ResetAsync()
    {
        await _lock.WaitAsync();
        try
        {
            using var command = _connection.CreateCommand();
            command.CommandText = "DELETE FROM synced_cards; DELETE FROM sync_runs;";
            await command.ExecuteNonQueryAsync();
        }
        finally
        {
            _lock.Release();
        }
    }

    // Copies sync-state.json into an empty database once, then renames the file to sync-state.imported.json.
    // Returns false (and changes nothing) if there is no file or the database already has data.
    public async Task<bool> ImportJsonAsync(string jsonPath)
    {
        if (!File.Exists(jsonPath))
            return false;

        var current = await LoadAsync();
        if (current.Cards.Count > 0 || current.History.Count > 0)
            return false;

        var state = await new JsonSyncStateStore(jsonPath).LoadAsync();
        await SaveAsync(state);
        File.Move(jsonPath, Path.ChangeExtension(jsonPath, ".imported.json"), overwrite: true);
        return true;
    }

    public void Dispose()
    {
        _connection.Dispose();
        _lock.Dispose();
    }

    private async Task SaveCardAsync(SqliteTransaction transaction, long assignmentId, SyncedCard card)
    {
        using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.Parameters.AddWithValue("@id", assignmentId);
        command.Parameters.AddWithValue("@card_id", card.CardId);
        command.Parameters.AddWithValue("@card_url", (object?)card.CardUrl ?? DBNull.Value);
        command.Parameters.AddWithValue("@done", card.Done ? 1 : 0);
        command.Parameters.AddWithValue("@synced_at", FormatDate(card.SyncedAt));

        // Try to UPDATE the card's row; if no row was changed, the card is new, so INSERT it
        command.CommandText = """
            UPDATE synced_cards
            SET card_id = @card_id, card_url = @card_url, done = @done, synced_at = @synced_at
            WHERE assignment_id = @id
            """;
        if (await command.ExecuteNonQueryAsync() > 0)
            return;

        command.CommandText = """
            INSERT INTO synced_cards (assignment_id, card_id, card_url, done, synced_at)
            VALUES (@id, @card_id, @card_url, @done, @synced_at)
            """;
        await command.ExecuteNonQueryAsync();
    }

    private async Task<DateTimeOffset?> GetNewestRunTimeAsync(SqliteTransaction transaction)
    {
        using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT ran_at FROM sync_runs ORDER BY id DESC LIMIT 1";
        return await command.ExecuteScalarAsync() is string ranAt ? ParseDate(ranAt) : null;
    }

    private async Task InsertRunAsync(SqliteTransaction transaction, SyncRun run)
    {
        using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO sync_runs (ran_at, trigger, created, moved, skipped)
            VALUES (@ran_at, @trigger, @created, @moved, @skipped)
            """;
        command.Parameters.AddWithValue("@ran_at", FormatDate(run.RanAt));
        command.Parameters.AddWithValue("@trigger", run.Trigger);
        command.Parameters.AddWithValue("@created", run.Created);
        command.Parameters.AddWithValue("@moved", run.Moved);
        command.Parameters.AddWithValue("@skipped", run.Skipped);
        await command.ExecuteNonQueryAsync();
    }

    // Keep only the newest MaxHistory runs, like the JSON file did
    private async Task TrimHistoryAsync(SqliteTransaction transaction)
    {
        using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            DELETE FROM sync_runs
            WHERE id NOT IN (SELECT id FROM sync_runs ORDER BY id DESC LIMIT @max)
            """;
        command.Parameters.AddWithValue("@max", SyncState.MaxHistory);
        await command.ExecuteNonQueryAsync();
    }

    // Dates are stored as ISO 8601 text with the time zone, e.g. 2026-09-29T10:42:00.0000000-04:00
    private static string FormatDate(DateTimeOffset date) => date.ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseDate(string text) =>
        DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
