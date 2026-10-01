using CanvasTrelloSync.Models;
using CanvasTrelloSync.Services;
using CanvasTrelloSync.Tests.Fakes;

namespace CanvasTrelloSync.Tests;

// Runs the shared store tests on an in-memory SQLite database, plus the JSON import.
public class SqliteSyncStateStoreTests : SyncStateStoreTests, IDisposable
{
    private readonly List<SqliteSyncStateStore> _stores = new();
    private readonly string _tempDir;
    private readonly string _jsonPath;

    public SqliteSyncStateStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "cts-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);
        _jsonPath = Path.Combine(_tempDir, "sync-state.json");
    }

    public void Dispose()
    {
        foreach (var store in _stores)
            store.Dispose();
        Directory.Delete(_tempDir, recursive: true);
    }

    // ":memory:" is a new, empty database that lives only as long as the store's connection
    protected override ISyncStateStore CreateStore() => CreateSqliteStore();

    private SqliteSyncStateStore CreateSqliteStore()
    {
        var store = new SqliteSyncStateStore("Data Source=:memory:");
        _stores.Add(store);
        return store;
    }

    private async Task WriteJsonStateAsync(int cardCount, int runCount)
    {
        var state = new SyncState();
        for (int i = 1; i <= cardCount; i++)
            state.Cards[i] = new SyncedCard { CardId = "card" + i, SyncedAt = DateTimeOffset.Now };
        for (int i = 0; i < runCount; i++)
            state.History.Add(new SyncRun { RanAt = DateTimeOffset.Now.AddMinutes(i), Trigger = "terminal" });
        await new JsonSyncStateStore(_jsonPath).SaveAsync(state);
    }

    [Fact]
    public async Task ImportJsonAsync_EmptyDatabase_CopiesStateAndRenamesFile()
    {
        await WriteJsonStateAsync(cardCount: 3, runCount: 2);
        var store = CreateSqliteStore();

        bool imported = await store.ImportJsonAsync(_jsonPath);
        var state = await store.LoadAsync();

        Assert.True(imported);
        Assert.Equal(3, state.Cards.Count);
        Assert.Equal(2, state.History.Count);
        Assert.False(File.Exists(_jsonPath));
        Assert.True(File.Exists(Path.Combine(_tempDir, "sync-state.imported.json")));
    }

    [Fact]
    public async Task ImportJsonAsync_NoJsonFile_ReturnsFalse()
    {
        var store = CreateSqliteStore();

        bool imported = await store.ImportJsonAsync(_jsonPath);

        Assert.False(imported);
        Assert.Empty((await store.LoadAsync()).Cards);
    }

    [Fact]
    public async Task ImportJsonAsync_DatabaseHasData_KeepsDatabaseAndFile()
    {
        await WriteJsonStateAsync(cardCount: 3, runCount: 0);
        var store = CreateSqliteStore();
        var existing = new SyncState();
        existing.Cards[500] = new SyncedCard { CardId = "db-card", SyncedAt = DateTimeOffset.Now };
        await store.SaveAsync(existing);

        bool imported = await store.ImportJsonAsync(_jsonPath);

        Assert.False(imported);
        Assert.Equal(500, Assert.Single((await store.LoadAsync()).Cards).Key);
        Assert.True(File.Exists(_jsonPath));
    }

    [Fact]
    public async Task SyncAsync_SecondSyncWithSqlite_CreatesZeroCards()
    {
        var source = new FakeTaskSource();
        var board = new FakeTaskBoard();
        source.AddTodo(101);
        source.AddTodo(102);
        var sync = new SyncService(source, board, CreateSqliteStore());

        await sync.SyncAsync(dryRun: false, trigger: "terminal");
        var second = await sync.SyncAsync(dryRun: false, trigger: "web");

        Assert.Empty(second.Created);
        Assert.Equal(2, board.CardsIn("Course 1").Count);
        Assert.Equal(2, (await sync.GetStateAsync()).History.Count);
    }
}
