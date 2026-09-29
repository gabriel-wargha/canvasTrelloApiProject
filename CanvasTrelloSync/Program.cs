using CanvasTrelloSync.Clients;
using CanvasTrelloSync.Services;
using CanvasTrelloSync.UI;
using Microsoft.Extensions.Configuration;
using Spectre.Console;

// 1. Load secrets from dotnet user-secrets (stored outside the repo)
IConfiguration config = new ConfigurationBuilder()
    .AddUserSecrets<Program>()
    .Build();

// 2. Read the flags
bool dryRun = args.Contains("--dry-run");
bool syncOnce = args.Contains("--sync");

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
var menu = new Menu(sync, board, dryRun);

// 5. --sync runs once and exits; otherwise open the menu
return syncOnce ? await menu.SyncOnceAsync() : await menu.RunAsync();
