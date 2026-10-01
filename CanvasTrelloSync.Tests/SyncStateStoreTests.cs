using CanvasTrelloSync.Models;
using CanvasTrelloSync.Services;

namespace CanvasTrelloSync.Tests;

// The same tests for every ISyncStateStore (JSON file, SQLite). Each store gets a subclass that says how to make one.
public abstract class SyncStateStoreTests
{
    private static readonly DateTimeOffset _syncedAt = new(2026, 9, 29, 10, 42, 0, TimeSpan.FromHours(-4));

    // Each call returns a fresh, empty store
    protected abstract ISyncStateStore CreateStore();

    [Fact]
    public async Task LoadAsync_EmptyStore_ReturnsEmptyState()
    {
        var state = await CreateStore().LoadAsync();

        Assert.Empty(state.Cards);
        Assert.Empty(state.History);
    }

    [Fact]
    public async Task SaveAsync_ThenLoadAsync_ReturnsSameData()
    {
        var store = CreateStore();
        var state = new SyncState();
        state.Cards[101] = new SyncedCard { CardId = "abc", CardUrl = "https://trello.test/c/abc", Done = true, SyncedAt = _syncedAt };
        state.History.Add(new SyncRun { RanAt = _syncedAt, Trigger = "web", Created = 3, Moved = 1, Skipped = 16 });

        await store.SaveAsync(state);
        var loaded = await store.LoadAsync();

        var card = loaded.Cards[101];
        Assert.Equal("abc", card.CardId);
        Assert.Equal("https://trello.test/c/abc", card.CardUrl);
        Assert.True(card.Done);
        Assert.Equal(_syncedAt, card.SyncedAt);

        var run = Assert.Single(loaded.History);
        Assert.Equal(_syncedAt, run.RanAt);
        Assert.Equal("web", run.Trigger);
        Assert.Equal((3, 1, 16), (run.Created, run.Moved, run.Skipped));
    }

    [Fact]
    public async Task SaveAsync_CardWithoutUrl_LoadsNullUrl()
    {
        var store = CreateStore();
        var state = new SyncState();
        state.Cards[7] = new SyncedCard { CardId = "x", CardUrl = null, SyncedAt = _syncedAt };

        await store.SaveAsync(state);

        Assert.Null((await store.LoadAsync()).Cards[7].CardUrl);
    }

    [Fact]
    public async Task SaveAsync_CardChanged_LoadsNewValues()
    {
        var store = CreateStore();
        var state = new SyncState();
        state.Cards[101] = new SyncedCard { CardId = "abc", Done = false, SyncedAt = _syncedAt };
        await store.SaveAsync(state);

        state.Cards[101].Done = true;
        state.Cards[101].SyncedAt = _syncedAt.AddDays(1);
        await store.SaveAsync(state);
        var card = Assert.Single((await store.LoadAsync()).Cards).Value;

        Assert.True(card.Done);
        Assert.Equal(_syncedAt.AddDays(1), card.SyncedAt);
    }

    [Fact]
    public async Task SaveAsync_SameHistorySavedTwice_DoesNotDuplicateRuns()
    {
        var store = CreateStore();
        var state = new SyncState();
        state.History.Add(new SyncRun { RanAt = _syncedAt, Trigger = "terminal", Created = 1 });
        await store.SaveAsync(state);

        // The next sync loads the state, adds one run and saves everything again
        state = await store.LoadAsync();
        state.History.Add(new SyncRun { RanAt = _syncedAt.AddHours(1), Trigger = "web", Created = 2 });
        await store.SaveAsync(state);
        var history = (await store.LoadAsync()).History;

        Assert.Equal(new[] { "terminal", "web" }, history.Select(run => run.Trigger));
    }

    [Fact]
    public async Task SaveAsync_HistoryTrimmed_LoadsOnlyKeptRuns()
    {
        var store = CreateStore();
        var state = new SyncState();
        for (int i = 0; i < SyncState.MaxHistory; i++)
            state.History.Add(new SyncRun { RanAt = _syncedAt.AddMinutes(i), Trigger = "terminal", Created = i });
        await store.SaveAsync(state);

        // Like SyncService: add a run, then drop the oldest to stay at MaxHistory
        state.History.Add(new SyncRun { RanAt = _syncedAt.AddMinutes(SyncState.MaxHistory), Trigger = "web", Created = 99 });
        state.History.RemoveAt(0);
        await store.SaveAsync(state);
        var history = (await store.LoadAsync()).History;

        Assert.Equal(SyncState.MaxHistory, history.Count);
        Assert.Equal(1, history.First().Created);
        Assert.Equal(99, history.Last().Created);
    }

    [Fact]
    public async Task ResetAsync_AfterSave_LoadsEmptyState()
    {
        var store = CreateStore();
        var state = new SyncState();
        state.Cards[101] = new SyncedCard { CardId = "abc", SyncedAt = _syncedAt };
        state.History.Add(new SyncRun { RanAt = _syncedAt, Trigger = "web" });
        await store.SaveAsync(state);

        await store.ResetAsync();
        var loaded = await store.LoadAsync();

        Assert.Empty(loaded.Cards);
        Assert.Empty(loaded.History);
    }
}
