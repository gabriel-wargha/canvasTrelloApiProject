using System.Diagnostics;
using System.Net;
using System.Text.Json;
using CanvasTrelloSync.Interfaces;
using CanvasTrelloSync.Models;
using CanvasTrelloSync.Services;
using CanvasTrelloSync.Web;
using Spectre.Console;

namespace CanvasTrelloSync.UI;

// The terminal app: header, arrow-key menu, and one screen per menu item.
public class Menu
{
    private const string ListCourses = "📚  List my courses";
    private const string ListAssignments = "📝  List assignments";
    private const string PreviewSync = "👀  Preview sync (dry run)";
    private const string RealSync = "🔄  Sync to Trello";
    private const string DryRunSync = "🔄  Sync to Trello (preview only)";
    private const string OpenDashboard = "🌐  Open web dashboard";
    private const string History = "🕒  Sync history";
    private const string Reset = "🧹  Reset sync history";
    private const string Exit = "🚪  Exit";

    private readonly SyncService _sync;
    private readonly ITaskBoard _board;
    private readonly DashboardServer _dashboard;
    private readonly bool _dryRun;
    private string _boardName = "?";

    public Menu(SyncService sync, ITaskBoard board, DashboardServer dashboard, bool dryRun)
    {
        _sync = sync;
        _board = board;
        _dashboard = dashboard;
        _dryRun = dryRun;
    }

    // The interactive menu. Returns the exit code.
    public async Task<int> RunAsync()
    {
        if (!await ConnectAsync())
            return 1;

        while (true)
        {
            AnsiConsole.Clear();
            await ShowHeaderAsync();

            string choice = AnsiConsole.Prompt(new SelectionPrompt<string>()
                .Title("What do you want to do?")
                .HighlightStyle(new Style(Color.DodgerBlue1))
                .AddChoices(ListCourses, ListAssignments, PreviewSync, _dryRun ? DryRunSync : RealSync, OpenDashboard, History, Reset, Exit));

            if (choice == Exit)
            {
                await _dashboard.DisposeAsync();
                AnsiConsole.MarkupLine("👋 Bye!");
                return 0;
            }

            try
            {
                switch (choice)
                {
                    case ListCourses:
                        await ShowCoursesAsync();
                        break;
                    case ListAssignments:
                        await ShowAssignmentsAsync();
                        break;
                    case PreviewSync:
                        await SyncWithProgressAsync(dryRun: true);
                        break;
                    case RealSync:
                    case DryRunSync:
                        await SyncWithProgressAsync(_dryRun);
                        break;
                    case OpenDashboard:
                        await OpenDashboardAsync();
                        break;
                    case History:
                        await ShowHistoryAsync();
                        break;
                    case Reset:
                        await ResetAsync();
                        break;
                }
            }
            catch (Exception ex) when (IsExpected(ex))
            {
                // Show the problem and go back to the menu instead of crashing
                ShowError(ex);
            }

            AnsiConsole.MarkupLine("\n[grey]Press any key to go back to the menu...[/]");
            Console.ReadKey(intercept: true);
        }
    }

    // --sync: one sync with the same progress bar and result, no menu. Returns the exit code.
    public async Task<int> SyncOnceAsync()
    {
        if (!await ConnectAsync())
            return 1;

        await ShowHeaderAsync();
        try
        {
            await SyncWithProgressAsync(_dryRun);
            return 0;
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            ShowError(ex);
            return 1;
        }
    }

    // Reads the board name once at startup; this also catches a bad Trello key or board id early
    private async Task<bool> ConnectAsync()
    {
        try
        {
            _boardName = await AnsiConsole.Status().StartAsync("Connecting to Trello...", _ => _board.GetBoardNameAsync());
            return true;
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            ShowError(ex);
            return false;
        }
    }

    private async Task ShowHeaderAsync()
    {
        // The big title needs about 90 columns; a narrow window gets a simple title line instead of a broken one
        if (AnsiConsole.Profile.Width >= 90)
            AnsiConsole.Write(new FigletText("CanvasTrello").Color(Color.DodgerBlue1));
        else
            AnsiConsole.Write(new Rule("[bold dodgerblue1]CanvasTrelloSync[/]").LeftJustified());

        string lastSync;
        try
        {
            var lastRun = (await _sync.GetStateAsync()).History.LastOrDefault();
            lastSync = lastRun is null ? "never" : lastRun.RanAt.ToLocalTime().ToString("MMM d, HH:mm");
        }
        catch (JsonException)
        {
            // Keep the menu usable so "Reset sync history" can fix it
            lastSync = "[red]sync-state.json is damaged[/]";
        }

        AnsiConsole.MarkupLine(
            $"[grey]Source:[/] Canvas Network   [grey]Board:[/] {Markup.Escape(_boardName)}   [grey]Last sync:[/] {lastSync}");
        if (_dashboard.IsRunning)
            AnsiConsole.MarkupLine($"[grey]Dashboard:[/] [link]{_dashboard.Url}[/]");
        if (_dryRun)
            AnsiConsole.MarkupLine("[black on yellow] DRY RUN [/] [yellow]Every sync is a preview: nothing changes on Trello or in sync-state.json.[/]");
        AnsiConsole.WriteLine();
    }

    private async Task ShowCoursesAsync()
    {
        var canvas = await LoadCanvasAsync();

        var table = new Table().Border(TableBorder.Rounded).Title("[bold]My courses[/]");
        table.AddColumn("Code");
        table.AddColumn("Name");
        table.AddColumn(new TableColumn("To do").RightAligned());
        table.AddColumn(new TableColumn("Done").RightAligned());

        // Courses with no assignments are just noise, so hide them
        foreach (var item in canvas.Courses.Where(c => c.Assignments.Count > 0))
        {
            int done = item.Assignments.Count(a => a.IsSubmitted);
            table.AddRow(
                Markup.Escape(item.Course.CourseCode ?? "?"),
                Markup.Escape(item.Course.Name ?? "(no name)"),
                $"[yellow]{item.Assignments.Count - done}[/]",
                $"[green]{done}[/]");
        }

        AnsiConsole.Write(table);
        ShowFailedCourses(canvas.FailedCourses);
    }

    private async Task ShowAssignmentsAsync()
    {
        var canvas = await LoadCanvasAsync();
        var state = await _sync.GetStateAsync();

        var table = new Table().Border(TableBorder.Rounded).Title("[bold]Assignments[/]");
        table.AddColumn("Status");
        table.AddColumn("Course");
        table.AddColumn("Assignment");
        table.AddColumn("Card");

        // To-do first
        foreach (var a in canvas.AllAssignments.OrderBy(a => a.IsSubmitted).ThenBy(a => a.CourseCode))
        {
            string card = !state.Cards.TryGetValue(a.Id, out var synced) ? "[grey]—[/]"
                : synced.Done ? "[green]Done[/]"
                : "[blue]Later[/]";

            table.AddRow(
                a.IsSubmitted ? "[green]✅ done[/]" : "[yellow]⏳ todo[/]",
                Markup.Escape(a.CourseCode ?? "?"),
                Markup.Escape(a.Name ?? "(no name)"),
                card);
        }

        AnsiConsole.Write(table);
        int todo = canvas.AllAssignments.Count(a => !a.IsSubmitted);
        AnsiConsole.MarkupLine($"Total: [bold]{canvas.AllAssignments.Count()}[/] assignments, [yellow]{todo} to do[/].");
        ShowFailedCourses(canvas.FailedCourses);
    }

    private async Task SyncWithProgressAsync(bool dryRun)
    {
        string verb = dryRun ? "Previewing" : "Syncing";

        var result = await AnsiConsole.Progress()
            .AutoClear(false)
            .Columns(new SpinnerColumn(), new TaskDescriptionColumn(), new ProgressBarColumn())
            .StartAsync(async ctx =>
            {
                // Spins without a bar while Canvas loads, because we don't know the total yet
                var task = ctx.AddTask("Loading from Canvas...");
                task.IsIndeterminate = true;

                return await _sync.SyncAsync(dryRun, "terminal", (done, total) =>
                {
                    task.IsIndeterminate = false;
                    task.MaxValue = Math.Max(total, 1);   // a bar of 0 would never look finished
                    task.Value = total == 0 ? 1 : done;
                    task.Description = $"{verb}... {done}/{total}";
                });
            });

        ShowResult(result);
    }

    private static void ShowResult(SyncResult result)
    {
        string created = result.DryRun ? "Would create" : "Created";
        string moved = result.DryRun ? "Would move to Done" : "Moved to Done";

        AnsiConsole.Write(new Panel(
                $"✨ {created} [bold]{result.Created.Count}[/]   ➜ {moved} [bold]{result.Moved.Count}[/]   ⏭ Skipped [bold]{result.Skipped}[/]")
            .Header(result.DryRun ? "Result (dry run)" : "Result")
            .Border(BoxBorder.Rounded)
            .BorderColor(result.DryRun ? Color.Yellow : Color.Green));

        if (result.Created.Count + result.Moved.Count > 0)
        {
            var table = new Table().Border(TableBorder.Rounded);
            table.AddColumn("Card");
            table.AddColumn("Course");
            table.AddColumn("Assignment");

            foreach (var a in result.Created)
                table.AddRow("[blue]✨ new in Later[/]", Markup.Escape(a.CourseCode ?? "?"), Markup.Escape(a.Name ?? "(no name)"));
            foreach (var a in result.Moved)
                table.AddRow("[green]➜ to Done[/]", Markup.Escape(a.CourseCode ?? "?"), Markup.Escape(a.Name ?? "(no name)"));

            AnsiConsole.Write(table);
        }

        ShowFailedCourses(result.FailedCourses);
        if (result.DryRun)
            AnsiConsole.MarkupLine("[yellow]Dry run: nothing was changed.[/]");
    }

    private async Task OpenDashboardAsync()
    {
        // The server starts once and keeps running while the menu is open
        await _dashboard.StartAsync();
        AnsiConsole.MarkupLine($"[green]Dashboard running at[/] [link]{_dashboard.Url}[/]");

        // UseShellExecute hands the URL to macOS/Windows, which opens the default browser
        Process.Start(new ProcessStartInfo(_dashboard.Url) { UseShellExecute = true });
    }

    private async Task ShowHistoryAsync()
    {
        var history = (await _sync.GetStateAsync()).History;
        if (history.Count == 0)
        {
            AnsiConsole.MarkupLine("[grey]No syncs yet. (Previews are not saved in the history.)[/]");
            return;
        }

        var table = new Table().Border(TableBorder.Rounded).Title("[bold]Sync history[/]");
        table.AddColumn("When");
        table.AddColumn("From");
        table.AddColumn(new TableColumn("Created").RightAligned());
        table.AddColumn(new TableColumn("Moved").RightAligned());
        table.AddColumn(new TableColumn("Skipped").RightAligned());

        // Newest first
        foreach (var run in Enumerable.Reverse(history))
        {
            table.AddRow(
                run.RanAt.ToLocalTime().ToString("MMM d, HH:mm"),
                Markup.Escape(run.Trigger),
                $"[blue]+{run.Created}[/]",
                $"[green]{run.Moved}[/]",
                $"[grey]{run.Skipped}[/]");
        }

        AnsiConsole.Write(table);
    }

    private async Task ResetAsync()
    {
        // A dry-run session must never change the sync state
        if (_dryRun)
        {
            AnsiConsole.MarkupLine("[yellow]Reset is turned off in dry-run mode, because a dry run never changes sync-state.json.[/]");
            return;
        }

        AnsiConsole.MarkupLine("[yellow]The app will forget which Trello cards it made.[/]");
        AnsiConsole.MarkupLine("[yellow]The next sync will create them again, so you may get duplicate cards.[/]");
        if (!AnsiConsole.Confirm("Delete the sync history?", defaultValue: false))
        {
            AnsiConsole.MarkupLine("[grey]Nothing was deleted.[/]");
            return;
        }

        await _sync.ResetAsync();
        AnsiConsole.MarkupLine("[green]Sync history deleted.[/]");
    }

    private Task<CanvasSnapshot> LoadCanvasAsync() =>
        AnsiConsole.Status().StartAsync("Loading from Canvas...", _ => _sync.LoadCanvasAsync());

    private static void ShowFailedCourses(List<string> failedCourses)
    {
        if (failedCourses.Count > 0)
            AnsiConsole.MarkupLine($"[yellow]Skipped (Canvas blocked access): {Markup.Escape(string.Join(", ", failedCourses))}[/]");
    }

    // Problems we expect and can explain; anything else is a real bug and should crash loudly
    private static bool IsExpected(Exception ex) =>
        ex is HttpRequestException or InvalidOperationException or JsonException or IOException;

    private static void ShowError(Exception ex)
    {
        string hint = ex switch
        {
            HttpRequestException { StatusCode: HttpStatusCode.Unauthorized } =>
                "A token is wrong or expired. Check Canvas:Token, Trello:ApiKey and Trello:ApiToken in user-secrets.",
            HttpRequestException { StatusCode: HttpStatusCode.NotFound } =>
                "Something was not found. Check Trello:BoardId and Canvas:BaseUrl in user-secrets.",
            HttpRequestException { StatusCode: null } =>
                "Could not reach the server. Check your internet connection.",
            JsonException => "sync-state.json is damaged. Fix or delete it (Reset sync history).",
            _ => "",
        };

        string body = $"[red]{Markup.Escape(ex.Message)}[/]" + (hint == "" ? "" : $"\n{Markup.Escape(hint)}");
        AnsiConsole.Write(new Panel(body).Header("[red]Error[/]").Border(BoxBorder.Rounded).BorderColor(Color.Red));
    }
}
