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
    public async Task GetSummary_CardMarkedDone_CountsItAsDone()
    {
        _source.AddTodo(101);
        _source.AddTodo(102);
        await _sync.SyncAsync(dryRun: false, trigger: "terminal");
        var state = await _store.LoadAsync();
        state.Cards[101].Done = true;
        await _store.SaveAsync(state);
        var client = await StartServerAsync();

        var summary = await client.GetFromJsonAsync<JsonElement>("/api/summary");

        Assert.Equal(1, summary.GetProperty("done").GetInt32());
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/app.js")]
    [InlineData("/styles.css")]
    public async Task GetPageFile_Always_TellsBrowserToCheckForNewVersion(string path)
    {
        var client = await StartServerAsync();

        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoCache);
    }

    [Fact]
    public async Task GetNext_TwoCourses_ReturnsOneNextAssignmentPerCourseWithPoints()
    {
        FakeTaskSource.Submit(_source.AddTodo(101, courseId: 1));
        var next = _source.AddTodo(102, courseId: 1);
        next.Points = 10;
        _source.AddTodo(103, courseId: 1);
        _source.AddTodo(201, courseId: 2);
        await _sync.SyncAsync(dryRun: false, trigger: "terminal");
        var client = await StartServerAsync();

        var json = await client.GetFromJsonAsync<JsonElement>("/api/next");

        Assert.Equal(2, json.GetArrayLength());
        var first = json[0];
        Assert.Equal("C1", first.GetProperty("course").GetString());
        Assert.Equal("Course 1", first.GetProperty("courseName").GetString());
        Assert.Equal(102, first.GetProperty("id").GetInt64());
        Assert.Equal(10, first.GetProperty("points").GetDouble());
        Assert.False(first.TryGetProperty("description", out _));
        Assert.Equal("https://trello.test/c/102", first.GetProperty("cardUrl").GetString());
        Assert.Equal(201, json[1].GetProperty("id").GetInt64());
    }

    [Fact]
    public async Task GetNext_CourseAllDone_LeavesItOut()
    {
        FakeTaskSource.Submit(_source.AddTodo(101, courseId: 1));
        _source.AddTodo(201, courseId: 2);
        var client = await StartServerAsync();

        var json = await client.GetFromJsonAsync<JsonElement>("/api/next");

        Assert.Equal(201, Assert.Single(json.EnumerateArray()).GetProperty("id").GetInt64());
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
