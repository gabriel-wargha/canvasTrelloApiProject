using CanvasTrelloSync.Clients;
using CanvasTrelloSync.Services;
using CanvasTrelloSync.UI;
using CanvasTrelloSync.Web;
using Microsoft.Extensions.Configuration;
using Spectre.Console;

// 1. Load secrets from dotnet user-secrets (stored outside the repo)
IConfiguration config = new ConfigurationBuilder()
    .AddUserSecrets<Program>()
    .Build();

// 2. Read the flags
bool dryRun = args.Contains("--dry-run");
bool syncOnce = args.Contains("--sync");
bool webOnly = args.Contains("--web");

// 3. Stop early with a clear message if a secret is missing (only the key names are shown, never values)
string[] requiredKeys = { "Canvas:BaseUrl", "Canvas:Token", "Trello:ApiKey", "Trello:ApiToken", "Trello:BoardId" };
var missing = requiredKeys.Where(key => string.IsNullOrWhiteSpace(config[key])).ToList();
if (missing.Count > 0)
{
    AnsiConsole.Write(new Panel($"[red]Missing in user-secrets:[/] {string.Join(", ", missing)}")
        .Header("[red]Error[/]").BorderColor(Color.Red));
    return 1;
}

// 4. Build the app's parts. sync-state.json lives in the folder you run from (CanvasTrelloSync/)
var source = new CanvasClient(config["Canvas:BaseUrl"]!, config["Canvas:Token"]!);
var board = new TrelloClient(config["Trello:ApiKey"]!, config["Trello:ApiToken"]!, config["Trello:BoardId"]!);
var store = new JsonSyncStateStore("sync-state.json");
var sync = new SyncService(source, board, store);
await using var dashboard = new DashboardServer(sync, board, dryRun);

// 5. --web: only the dashboard, until Ctrl+C
if (webOnly)
{
    AnsiConsole.Write(new Rule("[bold dodgerblue1]CanvasTrelloSync dashboard[/]").LeftJustified());
    if (dryRun)
        AnsiConsole.MarkupLine("[black on yellow] DRY RUN [/] [yellow]Sync now is a preview: nothing changes on Trello or in sync-state.json.[/]");
    AnsiConsole.MarkupLine($"Running at [link]{DashboardServer.DefaultUrl}[/]. Press [bold]Ctrl+C[/] to stop.");

    try
    {
        await dashboard.RunUntilStoppedAsync();
        return 0;
    }
    catch (IOException ex)
    {
        // Most often: port 5080 is already used (maybe the app is already running in another window)
        AnsiConsole.Write(new Panel($"[red]{Markup.Escape(ex.Message)}[/]").Header("[red]Error[/]").BorderColor(Color.Red));
        return 1;
    }
}

// 6. --sync runs once and exits; otherwise open the menu
var menu = new Menu(sync, board, dashboard, dryRun);
return syncOnce ? await menu.SyncOnceAsync() : await menu.RunAsync();
