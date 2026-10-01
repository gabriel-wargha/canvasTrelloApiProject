using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CanvasTrelloSync.Services;
using CanvasTrelloSync.Tests.Fakes;
using CanvasTrelloSync.Web;

namespace CanvasTrelloSync.Tests;

// Starts the real web server with fake Canvas/Trello and calls it over HTTP, like the browser will.
public class DashboardServerTests : IAsyncLifetime
{
    private readonly string _tempDir;
    private readonly FakeTaskSource _source = new();
    private readonly FakeTaskBoard _board = new();
    private readonly JsonSyncStateStore _store;
    private readonly SyncService _sync;
    private readonly List<DashboardServer> _servers = new();

    public DashboardServerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "cts-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);
        _store = new JsonSyncStateStore(Path.Combine(_tempDir, "sync-state.json"));
        _sync = new SyncService(_source, _board, _store);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var server in _servers)
            await server.DisposeAsync();
        Directory.Delete(_tempDir, recursive: true);
    }

    // Port 0 lets the system pick a free port, so tests never clash with a running app on 5080
    private async Task<HttpClient> StartServerAsync(bool dryRun = false)
    {
        var server = new DashboardServer(_sync, _board, dryRun, "http://127.0.0.1:0");
        _servers.Add(server);
        await server.StartAsync();
        return new HttpClient { BaseAddress = new Uri(server.Url) };
    }

    [Fact]
    public async Task GetSummary_TwoCourses_ReturnsProgressPerCourse()
    {
        _source.AddTodo(101, courseId: 1);
        FakeTaskSource.Submit(_source.AddTodo(102, courseId: 1));
        _source.AddTodo(201, courseId: 2);
        var client = await StartServerAsync();

        var json = await client.GetFromJsonAsync<JsonElement>("/api/summary");

        Assert.Equal("Fake board", json.GetProperty("boardName").GetString());
        Assert.False(json.GetProperty("dryRun").GetBoolean());
        Assert.Equal(3, json.GetProperty("total").GetInt32());
        Assert.Equal(1, json.GetProperty("done").GetInt32());
        var first = json.GetProperty("courses")[0];
        Assert.Equal("C1", first.GetProperty("code").GetString());
        Assert.Equal(1, first.GetProperty("done").GetInt32());
        Assert.Equal(2, first.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task GetAssignments_AfterSync_ShowsWhichListEachCardIsIn()
    {
        _source.AddTodo(101);
        await _sync.SyncAsync(dryRun: false, trigger: "terminal");
        FakeTaskSource.Submit(_source.AddTodo(102));
        var client = await StartServerAsync();

        var json = await client.GetFromJsonAsync<JsonElement>("/api/assignments");

        var byId = json.EnumerateArray().ToDictionary(a => a.GetProperty("id").GetInt64());
        Assert.Equal("Course 1", byId[101].GetProperty("card").GetString());
        Assert.Equal("https://trello.test/c/101", byId[101].GetProperty("cardUrl").GetString());
        Assert.True(byId[102].GetProperty("submitted").GetBoolean());
        Assert.Equal(JsonValueKind.Null, byId[102].GetProperty("card").ValueKind);
    }

    [Fact]
    public async Task GetBoard_DoneListMissing_ReturnsEmptyDoneWithoutCreatingIt()
    {
        _source.AddTodo(101);
        await _sync.SyncAsync(dryRun: false, trigger: "terminal");
        var client = await StartServerAsync();

        var json = await client.GetFromJsonAsync<JsonElement>("/api/board");

        var lists = json.GetProperty("lists");
        Assert.Equal(2, lists.GetArrayLength());
        Assert.Equal("Course 1", lists[0].GetProperty("name").GetString());
        Assert.Equal(1, lists[0].GetProperty("cards").GetArrayLength());
        Assert.Equal("Done", lists[1].GetProperty("name").GetString());
        Assert.Equal(0, lists[1].GetProperty("cards").GetArrayLength());
        Assert.False(_board.Lists.ContainsKey("Done"));
    }

    [Fact]
    public async Task GetBoard_TwoCoursesAndOldCards_ShowsCourseListsSharedLaterThenDone()
    {
        _source.AddTodo(101, courseId: 1);
        _source.AddTodo(201, courseId: 2);
        _board.AddCard("Later", 999);
        await _sync.SyncAsync(dryRun: false, trigger: "terminal");
        var client = await StartServerAsync();

        var json = await client.GetFromJsonAsync<JsonElement>("/api/board");

        var names = json.GetProperty("lists").EnumerateArray().Select(l => l.GetProperty("name").GetString());
        Assert.Equal(new[] { "Course 1", "Course 2", "Later", "Done" }, names);
    }

    [Fact]
    public async Task GetHistory_TwoRuns_ReturnsNewestFirst()
    {
        await _sync.SyncAsync(dryRun: false, trigger: "terminal");
        await _sync.SyncAsync(dryRun: false, trigger: "web");
        var client = await StartServerAsync();

        var json = await client.GetFromJsonAsync<JsonElement>("/api/history");

        Assert.Equal("web", json[0].GetProperty("trigger").GetString());
        Assert.Equal("terminal", json[1].GetProperty("trigger").GetString());
    }

    [Fact]
    public async Task PostSync_RealRun_AddsWebRowToHistory()
    {
        _source.AddTodo(101);
        var client = await StartServerAsync();

        using var response = await client.PostAsync("/api/sync", null);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(101, json.GetProperty("created")[0].GetProperty("id").GetInt64());
        var run = Assert.Single((await _store.LoadAsync()).History);
        Assert.Equal("web", run.Trigger);
        Assert.Single(_board.Cards);
    }

    [Fact]
    public async Task PostSync_DryRunServer_ReturnsPlanAndChangesNothing()
    {
        _source.AddTodo(101);
        var client = await StartServerAsync(dryRun: true);

        using var response = await client.PostAsync("/api/sync", null);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.True(json.GetProperty("dryRun").GetBoolean());
        Assert.Equal(1, json.GetProperty("created").GetArrayLength());
        Assert.Empty(_board.Cards);
        Assert.Empty((await _store.LoadAsync()).History);
    }

    [Fact]
    public async Task PostSync_FromAnotherWebsite_Returns403AndDoesNotSync()
    {
        _source.AddTodo(101);
        var client = await StartServerAsync();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/sync");
        request.Headers.Add("Origin", "https://evil.example");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(_board.Cards);
    }

    [Fact]
    public async Task GetSummary_CanvasFails_Returns502WithError()
    {
        _source.FailEverything = true;
        var client = await StartServerAsync();

        using var response = await client.GetAsync("/api/summary");
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal("Canvas returned 401 Unauthorized", json.GetProperty("error").GetString());
    }
}
