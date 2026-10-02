using System.Text.Json;
using CanvasTrelloSync.Interfaces;
using CanvasTrelloSync.Models;
using CanvasTrelloSync.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CanvasTrelloSync.Web;

// The web dashboard's server: a small JSON API (and, from Block 6, the page) inside the same app as the menu.
public class DashboardServer : IAsyncDisposable
{
    public const string DefaultUrl = "http://localhost:5080";

    private readonly SyncService _sync;
    private readonly ITaskBoard _board;
    private readonly bool _dryRun;
    private readonly string _listenUrl;
    private WebApplication? _app;

    public DashboardServer(SyncService sync, ITaskBoard board, bool dryRun, string listenUrl = DefaultUrl)
    {
        _sync = sync;
        _board = board;
        _dryRun = dryRun;
        _listenUrl = listenUrl;
        Url = listenUrl;
    }

    // The real address once started (tests use port 0, and the system picks a free port)
    public string Url { get; private set; }
    public bool IsRunning => _app != null;

    // Starts in the background next to the menu. Calling it again does nothing.
    public async Task StartAsync()
    {
        if (_app != null)
            return;

        var app = BuildApp(handleCtrlC: false);
        await app.StartAsync();
        _app = app;

        var addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>();
        Url = addresses?.Addresses.FirstOrDefault() ?? _listenUrl;
    }

    // --web: runs until Ctrl+C
    public async Task RunUntilStoppedAsync()
    {
        _app = BuildApp(handleCtrlC: true);
        await _app.RunAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_app != null)
            await _app.DisposeAsync();
        _app = null;
    }

    private WebApplication BuildApp(bool handleCtrlC)
    {
        // The page files are copied next to the app when it builds, so they're found from any folder (and in tests)
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            WebRootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot"),
        });
        builder.WebHost.UseUrls(_listenUrl);

        // No logs at all: they would print over the terminal menu
        builder.Logging.ClearProviders();

        // Only answer to localhost names, so another website can't reach this server by pointing its own name at 127.0.0.1
        builder.Configuration["AllowedHosts"] = "localhost;127.0.0.1";

        if (!handleCtrlC)
            builder.Services.AddSingleton<IHostLifetime, NoConsoleLifetime>();

        var app = builder.Build();
        app.Use(HandleErrorsAsync);
        app.UseDefaultFiles();
        app.UseStaticFiles(new StaticFileOptions
        {
            // "no-cache" = the browser may keep a copy, but must ask first whether it changed,
            // so a new app.js shows up on a normal reload
            OnPrepareResponse = ctx => ctx.Context.Response.Headers.CacheControl = "no-cache",
        });

        app.MapGet("/api/summary", GetSummaryAsync);
        app.MapGet("/api/assignments", GetAssignmentsAsync);
        app.MapGet("/api/board", GetBoardAsync);
        app.MapGet("/api/history", GetHistoryAsync);
        app.MapGet("/api/next", GetNextAsync);
        app.MapPost("/api/sync", PostSyncAsync);

        return app;
    }

    private async Task<IResult> GetSummaryAsync()
    {
        var canvas = await _sync.LoadCanvasAsync();
        var state = await _sync.GetStateAsync();

        var courses = canvas.Courses
            .Where(c => c.Assignments.Count > 0)
            .Select(c => new CourseProgress
            {
                Code = c.Course.CourseCode ?? c.Course.Id.ToString(),
                Name = c.Course.Name ?? "",
                Done = c.Assignments.Count(a => a.IsDone),
                Total = c.Assignments.Count,
            })
            .ToList();

        return Results.Ok(new
        {
            dryRun = _dryRun,
            boardName = await _board.GetBoardNameAsync(),
            lastSync = state.History.LastOrDefault()?.RanAt,
            total = courses.Sum(c => c.Total),
            done = courses.Sum(c => c.Done),
            courses,
            failedCourses = canvas.FailedCourses,
        });
    }

    private async Task<IResult> GetAssignmentsAsync()
    {
        var canvas = await _sync.LoadCanvasAsync();
        var state = await _sync.GetStateAsync();

        var assignments = canvas.AllAssignments
            .OrderBy(a => a.IsDone)
            .ThenBy(a => a.CourseCode)
            .Select(a =>
            {
                state.Cards.TryGetValue(a.Id, out var card);
                return new
                {
                    id = a.Id,
                    name = a.Name,
                    course = a.CourseCode,
                    canvasUrl = a.Url,
                    submitted = a.IsDone,   // submitted on Canvas, or marked done by me
                    card = card is null ? null : card.Done ? SyncService.DoneList : CourseLists.ListName(a.CourseName, a.CourseCode),
                    cardUrl = card?.CardUrl,
                };
            });

        return Results.Ok(assignments);
    }

    private async Task<IResult> GetBoardAsync()
    {
        // GetListsAsync only reads; EnsureListAsync would create a missing Done list, which a page view must never do
        var lists = await _board.GetListsAsync();
        var result = new List<object>();

        var canvas = await _sync.LoadCanvasAsync();
        var state = await _sync.GetStateAsync();

        // The recommended next activity of each course, by its list name
        var nextByList = new Dictionary<string, Assignment>(StringComparer.OrdinalIgnoreCase);
        foreach (var course in canvas.Courses)
        {
            if (NextActivity.Pick(course.Assignments) is Assignment next)
                nextByList.TryAdd(CourseLists.ListName(course.Course.Name, course.Course.CourseCode), next);
        }

        // One column per course, then the old shared Later (only while old cards are still in it), then Done
        // A course list is a list named after one of my current courses
        var names = canvas.Courses
            .Select(c => CourseLists.ListName(c.Course.Name, c.Course.CourseCode))
            .Where(lists.ContainsKey)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
        names.Add(SyncService.LaterList);
        names.Add(SyncService.DoneList);

        foreach (string name in names)
        {
            var cards = lists.TryGetValue(name, out string? listId)
                ? await _board.GetCardsAsync(listId)
                : new List<TrelloCard>();

            if (name == SyncService.LaterList && cards.Count == 0)
                continue;

            nextByList.TryGetValue(name, out var nextActivity);
            SyncedCard? nextCard = null;
            if (nextActivity != null)
                state.Cards.TryGetValue(nextActivity.Id, out nextCard);

            result.Add(new
            {
                name,
                cards = cards.Select(c => new { id = c.Id, name = c.Name, url = c.Url }),
                next = nextActivity is null ? null : new
                {
                    id = nextActivity.Id,
                    name = nextActivity.Name,
                    canvasUrl = nextActivity.Url,
                    cardId = nextCard?.CardId,
                },
            });
        }

        return Results.Ok(new { lists = result });
    }

    // The Next tab: one recommended assignment per course, with its points
    private async Task<IResult> GetNextAsync()
    {
        var canvas = await _sync.LoadCanvasAsync();
        var state = await _sync.GetStateAsync();
        var result = new List<object>();

        foreach (var course in canvas.Courses)
        {
            if (NextActivity.Pick(course.Assignments) is not Assignment next)
                continue;

            state.Cards.TryGetValue(next.Id, out var card);
            result.Add(new
            {
                course = next.CourseCode,
                courseName = course.Course.Name,
                id = next.Id,
                name = next.Name,
                points = next.Points,
                canvasUrl = next.Url,
                cardUrl = card?.CardUrl,
            });
        }

        return Results.Ok(result);
    }

    private async Task<IResult> GetHistoryAsync()
    {
        var history = (await _sync.GetStateAsync()).History;
        return Results.Ok(Enumerable.Reverse(history));
    }

    private async Task<IResult> PostSyncAsync(HttpRequest request)
    {
        // Takes HttpRequest, not HttpContext: with HttpContext, ASP.NET treats this as a raw handler and drops the returned result.
        // Browsers send Origin on a POST. Refuse other websites, so a page open in another tab can't start a real sync.
        // curl sends no Origin, and that's fine.
        string? origin = request.Headers.Origin;
        string ownOrigin = $"{request.Scheme}://{request.Host}";
        if (!string.IsNullOrEmpty(origin) && !string.Equals(origin, ownOrigin, StringComparison.OrdinalIgnoreCase))
            return Results.Json(new { error = "Sync is only allowed from the dashboard page." }, statusCode: StatusCodes.Status403Forbidden);

        var result = await _sync.SyncAsync(_dryRun, "web");

        return Results.Ok(new
        {
            dryRun = result.DryRun,
            created = result.Created.Select(a => new { id = a.Id, name = a.Name, course = a.CourseCode }),
            moved = result.Moved.Select(a => new { id = a.Id, name = a.Name, course = a.CourseCode }),
            regrouped = result.Regrouped.Select(a => new { id = a.Id, name = a.Name, course = a.CourseCode }),
            skipped = result.Skipped,
            failedCourses = result.FailedCourses,
        });
    }

    // Canvas/Trello problems become 502 (the problem is upstream); anything else becomes 500.
    // Client error messages hold only the URL path, never the tokens, so they are safe to return.
    private static async Task HandleErrorsAsync(HttpContext context, RequestDelegate next)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException)
        {
            context.Response.StatusCode = StatusCodes.Status502BadGateway;
            await context.Response.WriteAsJsonAsync(new { error = ex.Message });
        }
        catch (JsonException)
        {
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await context.Response.WriteAsJsonAsync(new { error = "sync-state.json is damaged. Fix or delete it (Reset sync history)." });
        }
        catch (SqliteException)
        {
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await context.Response.WriteAsJsonAsync(new { error = "canvas-trello.db can't be read. Is another program using it?" });
        }
    }
}
