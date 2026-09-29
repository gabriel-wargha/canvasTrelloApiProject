using CanvasTrelloSync.Models;
using CanvasTrelloSync.Services;

namespace CanvasTrelloSync.Tests;

public class JsonSyncStateStoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _statePath;

    public JsonSyncStateStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "cts-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);
        _statePath = Path.Combine(_tempDir, "sync-state.json");
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Fact]
    public async Task LoadAsync_NoFile_ReturnsEmptyState()
    {
        var state = await new JsonSyncStateStore(_statePath).LoadAsync();

        Assert.Empty(state.Cards);
        Assert.Empty(state.History);
    }

    [Fact]
    public async Task SaveAsync_ThenLoadAsync_ReturnsSameData()
    {
        var store = new JsonSyncStateStore(_statePath);
        var syncedAt = new DateTimeOffset(2026, 9, 29, 10, 42, 0, TimeSpan.Zero);
        var state = new SyncState();
        state.Cards[101] = new SyncedCard { CardId = "abc", CardUrl = "https://trello.test/c/abc", Done = true, SyncedAt = syncedAt };
        state.History.Add(new SyncRun { RanAt = syncedAt, Trigger = "web", Created = 3, Moved = 1, Skipped = 16 });

        await store.SaveAsync(state);
        var loaded = await store.LoadAsync();

        var card = loaded.Cards[101];
        Assert.Equal("abc", card.CardId);
        Assert.Equal("https://trello.test/c/abc", card.CardUrl);
        Assert.True(card.Done);
        Assert.Equal(syncedAt, card.SyncedAt);

        var run = Assert.Single(loaded.History);
        Assert.Equal("web", run.Trigger);
        Assert.Equal((3, 1, 16), (run.Created, run.Moved, run.Skipped));
    }

    [Fact]
    public async Task ResetAsync_FileExists_DeletesIt()
    {
        var store = new JsonSyncStateStore(_statePath);
        await store.SaveAsync(new SyncState());

        await store.ResetAsync();

        Assert.False(File.Exists(_statePath));
    }

    [Fact]
    public async Task ResetAsync_NoFile_DoesNotThrow()
    {
        await new JsonSyncStateStore(_statePath).ResetAsync();

        Assert.False(File.Exists(_statePath));
    }
}
