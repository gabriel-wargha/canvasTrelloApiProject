using CanvasTrelloSync.Models;
using CanvasTrelloSync.Services;
using CanvasTrelloSync.Tests.Fakes;

namespace CanvasTrelloSync.Tests;

public class SyncServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _statePath;
    private readonly FakeTaskSource _source = new();
    private readonly FakeTaskBoard _board = new();
    private readonly JsonSyncStateStore _store;
    private readonly SyncService _sync;

    public SyncServiceTests()
    {
        // Each test gets its own folder, so tests never share a sync-state.json
        _tempDir = Path.Combine(Path.GetTempPath(), "cts-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);
        _statePath = Path.Combine(_tempDir, "sync-state.json");

        _store = new JsonSyncStateStore(_statePath);
        _sync = new SyncService(_source, _board, _store);
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Fact]
    public async Task SyncAsync_NewTodoAssignment_CreatesCardInLater()
    {
        _source.AddTodo(101);

        var result = await _sync.SyncAsync(dryRun: false, trigger: "terminal");

        Assert.Single(result.Created);
        var card = Assert.Single(_board.CardsIn("Course 1"));
        Assert.Equal("[C1] Assignment 101", card.Name);

        var state = await _store.LoadAsync();
        Assert.Equal(card.Id, state.Cards[101].CardId);
        Assert.Equal(card.Url, state.Cards[101].CardUrl);
        Assert.False(state.Cards[101].Done);
    }

    [Fact]
    public async Task SyncAsync_RunTwice_SecondRunCreatesNothing()
    {
        _source.AddTodo(101);
        _source.AddTodo(102);
        await _sync.SyncAsync(dryRun: false, trigger: "terminal");

        var second = await _sync.SyncAsync(dryRun: false, trigger: "terminal");

        Assert.Empty(second.Created);
        Assert.Equal(2, second.Skipped);
        Assert.Equal(2, _board.Cards.Count);
    }

    [Fact]
    public async Task SyncAsync_SubmittedAfterCardCreated_MovesCardToDone()
    {
        var assignment = _source.AddTodo(101);
        await _sync.SyncAsync(dryRun: false, trigger: "terminal");

        FakeTaskSource.Submit(assignment);
        var result = await _sync.SyncAsync(dryRun: false, trigger: "terminal");

        Assert.Single(result.Moved);
        Assert.Single(_board.CardsIn("Done"));
        Assert.Empty(_board.CardsIn("Course 1"));
        Assert.True((await _store.LoadAsync()).Cards[101].Done);
    }

    [Fact]
    public async Task SyncAsync_CardAlreadyDone_DoesNotMoveAgain()
    {
        var assignment = _source.AddTodo(101);
        await _sync.SyncAsync(dryRun: false, trigger: "terminal");
        FakeTaskSource.Submit(assignment);
        await _sync.SyncAsync(dryRun: false, trigger: "terminal");
        int writesBefore = _board.WriteCalls;

        var third = await _sync.SyncAsync(dryRun: false, trigger: "terminal");

        Assert.Empty(third.Moved);
        Assert.Equal(1, third.Skipped);
        Assert.Equal(writesBefore, _board.WriteCalls);
    }

    [Fact]
    public async Task SyncAsync_SubmittedWithoutCard_CreatesCardInDone()
    {
        FakeTaskSource.Submit(_source.AddTodo(101));

        var result = await _sync.SyncAsync(dryRun: false, trigger: "terminal");

        Assert.Equal(101, Assert.Single(result.Created).Id);
        Assert.Empty(result.Moved);
        Assert.Single(_board.CardsIn("Done"));
        Assert.Empty(_board.CardsIn("Course 1"));
        Assert.True((await _store.LoadAsync()).Cards[101].Done);
    }

    [Fact]
    public async Task SyncAsync_SubmittedWithoutCardSyncedTwice_CreatesItOnce()
    {
        FakeTaskSource.Submit(_source.AddTodo(101));
        await _sync.SyncAsync(dryRun: false, trigger: "terminal");

        var second = await _sync.SyncAsync(dryRun: false, trigger: "terminal");

        Assert.Empty(second.Created);
        Assert.Empty(second.Moved);
        Assert.Equal(1, second.Skipped);
        Assert.Single(_board.Cards);
    }

    [Fact]
    public async Task SyncAsync_DryRunSubmittedWithoutCard_ReportsButWritesNothing()
    {
        FakeTaskSource.Submit(_source.AddTodo(101));

        var result = await _sync.SyncAsync(dryRun: true, trigger: "terminal");

        Assert.Equal(101, Assert.Single(result.Created).Id);
        Assert.Empty(_board.Cards);
        Assert.False(_board.Lists.ContainsKey("Done"));
        Assert.Equal(0, _board.WriteCalls);
    }

    [Fact]
    public async Task SyncAsync_DoneListMissing_CreatesIt()
    {
        var assignment = _source.AddTodo(101);
        await _sync.SyncAsync(dryRun: false, trigger: "terminal");
        Assert.False(_board.Lists.ContainsKey("Done"));

        FakeTaskSource.Submit(assignment);
        await _sync.SyncAsync(dryRun: false, trigger: "terminal");

        Assert.True(_board.Lists.ContainsKey("Done"));
    }

    [Fact]
    public async Task SyncAsync_SharedLaterListMissing_StillSyncs()
    {
        _board.Lists.Remove("Later");
        _source.AddTodo(101);

        var result = await _sync.SyncAsync(dryRun: false, trigger: "terminal");

        Assert.Single(result.Created);
        Assert.Single(_board.CardsIn("Course 1"));
    }

    [Fact]
    public async Task SyncAsync_TwoCourses_PutsEachCardInItsCourseList()
    {
        _source.AddTodo(101, courseId: 1);
        _source.AddTodo(102, courseId: 1);
        _source.AddTodo(201, courseId: 2);

        await _sync.SyncAsync(dryRun: false, trigger: "terminal");

        Assert.Equal(2, _board.CardsIn("Course 1").Count);
        Assert.Equal("card-201", Assert.Single(_board.CardsIn("Course 2")).Id);
        Assert.Empty(_board.CardsIn("Later"));
    }

    [Fact]
    public async Task SyncAsync_CourseListAlreadyExists_ReusesIt()
    {
        _board.Lists["Course 1"] = "list-existing";
        _source.AddTodo(101);

        await _sync.SyncAsync(dryRun: false, trigger: "terminal");

        Assert.Equal("list-existing", Assert.Single(_board.Cards).ListId);
    }

    [Fact]
    public async Task SyncAsync_DryRun_CreatesNoCourseList()
    {
        _source.AddTodo(101);

        await _sync.SyncAsync(dryRun: true, trigger: "terminal");

        Assert.False(_board.Lists.ContainsKey("Course 1"));
        Assert.Equal(0, _board.WriteCalls);
    }

    [Fact]
    public async Task SyncAsync_OldCardInSharedLater_MovesItToCourseList()
    {
        // A card made before course lists existed: open, and sitting in the shared "Later" list
        _source.AddTodo(101);
        var oldCard = _board.AddCard("Later", 101);
        await SaveOpenCardAsync(101, oldCard);

        var result = await _sync.SyncAsync(dryRun: false, trigger: "terminal");

        Assert.Equal(101, Assert.Single(result.Regrouped).Id);
        Assert.Empty(result.Created);
        Assert.Equal(0, result.Skipped);
        Assert.Empty(_board.CardsIn("Later"));
        Assert.Single(_board.CardsIn("Course 1"));
    }

    [Fact]
    public async Task SyncAsync_OldCardRegrouped_NextSyncSkipsIt()
    {
        _source.AddTodo(101);
        await SaveOpenCardAsync(101, _board.AddCard("Later", 101));
        await _sync.SyncAsync(dryRun: false, trigger: "terminal");
        int writesBefore = _board.WriteCalls;

        var second = await _sync.SyncAsync(dryRun: false, trigger: "terminal");

        Assert.Empty(second.Regrouped);
        Assert.Equal(1, second.Skipped);
        Assert.Equal(writesBefore, _board.WriteCalls);
    }

    [Fact]
    public async Task SyncAsync_DryRunWithOldCard_ReportsRegroupButMovesNothing()
    {
        _source.AddTodo(101);
        await SaveOpenCardAsync(101, _board.AddCard("Later", 101));

        var result = await _sync.SyncAsync(dryRun: true, trigger: "terminal");

        Assert.Single(result.Regrouped);
        Assert.Single(_board.CardsIn("Later"));
        Assert.Equal(0, _board.WriteCalls);
    }

    [Fact]
    public async Task SyncAsync_OldCardSubmitted_MovesItToDoneNotCourseList()
    {
        var assignment = _source.AddTodo(101);
        await SaveOpenCardAsync(101, _board.AddCard("Later", 101));
        FakeTaskSource.Submit(assignment);

        var result = await _sync.SyncAsync(dryRun: false, trigger: "terminal");

        Assert.Single(result.Moved);
        Assert.Empty(result.Regrouped);
        Assert.Single(_board.CardsIn("Done"));
        Assert.False(_board.Lists.ContainsKey("Course 1"));
    }

    private async Task SaveOpenCardAsync(long assignmentId, TrelloCard card)
    {
        var state = await _store.LoadAsync();
        state.Cards[assignmentId] = new SyncedCard { CardId = card.Id, CardUrl = card.Url, SyncedAt = DateTimeOffset.Now };
        await _store.SaveAsync(state);
    }

    [Fact]
    public async Task SyncAsync_DryRun_ChangesNothingOnBoardButReportsPlan()
    {
        var submitted = _source.AddTodo(101);
        await _sync.SyncAsync(dryRun: false, trigger: "terminal");
        FakeTaskSource.Submit(submitted);
        _source.AddTodo(102);
        int writesBefore = _board.WriteCalls;

        var result = await _sync.SyncAsync(dryRun: true, trigger: "terminal");

        Assert.True(result.DryRun);
        Assert.Equal(102, Assert.Single(result.Created).Id);
        Assert.Equal(101, Assert.Single(result.Moved).Id);
        Assert.Equal(writesBefore, _board.WriteCalls);
        Assert.False(_board.Lists.ContainsKey("Done"));
    }

    [Fact]
    public async Task SyncAsync_DryRun_LeavesStateFileUnchanged()
    {
        var submitted = _source.AddTodo(101);
        await _sync.SyncAsync(dryRun: false, trigger: "terminal");
        FakeTaskSource.Submit(submitted);
        _source.AddTodo(102);
        string before = await File.ReadAllTextAsync(_statePath);

        await _sync.SyncAsync(dryRun: true, trigger: "terminal");

        Assert.Equal(before, await File.ReadAllTextAsync(_statePath));
    }

    [Fact]
    public async Task SyncAsync_DryRunWithNoStateFile_DoesNotCreateIt()
    {
        _source.AddTodo(101);

        await _sync.SyncAsync(dryRun: true, trigger: "terminal");

        Assert.False(File.Exists(_statePath));
    }

    [Fact]
    public async Task SyncAsync_RealRun_AddsHistoryRow()
    {
        _source.AddTodo(101);
        FakeTaskSource.Submit(_source.AddTodo(102));

        await _sync.SyncAsync(dryRun: false, trigger: "web");

        var run = Assert.Single((await _store.LoadAsync()).History);
        // 101 gets a card in its course list, and 102 (already submitted) gets a card straight in Done
        Assert.Equal("web", run.Trigger);
        Assert.Equal(2, run.Created);
        Assert.Equal(0, run.Moved);
        Assert.Equal(0, run.Skipped);
    }

    [Fact]
    public async Task SyncAsync_MoreThan50Runs_KeepsOnlyLast50()
    {
        for (int i = 0; i < 55; i++)
            await _sync.SyncAsync(dryRun: false, trigger: i == 54 ? "web" : "terminal");

        var history = (await _store.LoadAsync()).History;
        Assert.Equal(SyncState.MaxHistory, history.Count);
        Assert.Equal("web", history[^1].Trigger);
    }

    [Fact]
    public async Task SyncAsync_OneCourseFails_SyncsTheOtherCourses()
    {
        _source.AddTodo(101, courseId: 1);
        _source.AddTodo(201, courseId: 2);
        _source.FailingCourseIds.Add(1);

        var result = await _sync.SyncAsync(dryRun: false, trigger: "terminal");

        Assert.Equal(201, Assert.Single(result.Created).Id);
        Assert.Equal("C1", Assert.Single(result.FailedCourses));
    }

    [Fact]
    public async Task SyncAsync_BoardFailsMidway_KeepsCardsCreatedBeforeFailure()
    {
        _source.AddTodo(101);
        _source.AddTodo(102);
        _source.AddTodo(103);
        _board.FailOnCreateNumber = 3;

        await Assert.ThrowsAsync<HttpRequestException>(() => _sync.SyncAsync(dryRun: false, trigger: "terminal"));

        // The 2 cards that were made must be remembered, or the next sync would duplicate them
        _board.FailOnCreateNumber = null;
        var retry = await _sync.SyncAsync(dryRun: false, trigger: "terminal");
        Assert.Equal(103, Assert.Single(retry.Created).Id);
        Assert.Equal(3, _board.Cards.Count);
    }

    [Fact]
    public async Task SyncAsync_TwoSyncsAtOnce_CreatesEachCardOnce()
    {
        for (int id = 101; id <= 110; id++)
            _source.AddTodo(id);

        await Task.WhenAll(
            _sync.SyncAsync(dryRun: false, trigger: "terminal"),
            _sync.SyncAsync(dryRun: false, trigger: "web"));

        Assert.Equal(10, _board.Cards.Count);
        Assert.Equal(2, (await _store.LoadAsync()).History.Count);
    }

    [Fact]
    public async Task SyncAsync_WithProgress_ReportsEachAssignment()
    {
        _source.AddTodo(101);
        _source.AddTodo(102);
        FakeTaskSource.Submit(_source.AddTodo(103));
        var reports = new List<(int Done, int Total)>();

        await _sync.SyncAsync(dryRun: true, trigger: "terminal", onProgress: (done, total) => reports.Add((done, total)));

        Assert.Equal(new[] { (0, 3), (1, 3), (2, 3), (3, 3) }, reports);
    }

    [Fact]
    public async Task ResetAsync_AfterSync_ForgetsCardsAndHistory()
    {
        _source.AddTodo(101);
        await _sync.SyncAsync(dryRun: false, trigger: "terminal");

        await _sync.ResetAsync();

        var state = await _sync.GetStateAsync();
        Assert.Empty(state.Cards);
        Assert.Empty(state.History);
    }

    [Fact]
    public async Task LoadCanvasAsync_CardMarkedDoneButNotSubmitted_CountsAsDone()
    {
        _source.AddTodo(101);
        _source.AddTodo(102);
        await _sync.SyncAsync(dryRun: false, trigger: "terminal");
        await MarkDoneAsync(101);

        var canvas = await _sync.LoadCanvasAsync();

        var byId = canvas.AllAssignments.ToDictionary(a => a.Id);
        Assert.True(byId[101].IsDone);
        Assert.False(byId[101].IsSubmitted);
        Assert.False(byId[102].IsDone);
    }

    [Fact]
    public async Task SyncAsync_CardMarkedDoneButNotSubmitted_LeavesItAlone()
    {
        _source.AddTodo(101);
        await _sync.SyncAsync(dryRun: false, trigger: "terminal");
        await MarkDoneAsync(101);
        int writesBefore = _board.WriteCalls;

        var result = await _sync.SyncAsync(dryRun: false, trigger: "terminal");

        Assert.Empty(result.Created);
        Assert.Empty(result.Moved);
        Assert.Empty(result.Regrouped);
        Assert.Equal(1, result.Skipped);
        Assert.Equal(writesBefore, _board.WriteCalls);
    }

    private async Task MarkDoneAsync(long assignmentId)
    {
        var state = await _store.LoadAsync();
        state.Cards[assignmentId].Done = true;
        await _store.SaveAsync(state);
    }

    [Fact]
    public async Task LoadCanvasAsync_OneCourseFails_ReturnsOtherCoursesAndFailedName()
    {
        _source.AddTodo(101, courseId: 1);
        _source.AddTodo(201, courseId: 2);
        _source.FailingCourseIds.Add(1);

        var canvas = await _sync.LoadCanvasAsync();

        var course = Assert.Single(canvas.Courses);
        Assert.Equal(2, course.Course.Id);
        Assert.Equal(201, Assert.Single(canvas.AllAssignments).Id);
        Assert.Equal("C1", Assert.Single(canvas.FailedCourses));
    }
}
