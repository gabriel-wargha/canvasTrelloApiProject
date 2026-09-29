using CanvasTrelloSync.Clients;
using CanvasTrelloSync.Interfaces;
using CanvasTrelloSync.Models;
using Microsoft.Extensions.Configuration;
using Spectre.Console;

// 1. Load secrets from dotnet user-secrets (stored outside the repo)
IConfiguration config = new ConfigurationBuilder()
    .AddUserSecrets<Program>()
    .Build();

string? canvasUrl = config["Canvas:BaseUrl"];
string? canvasToken = config["Canvas:Token"];
bool dryRun = args.Contains("--dry-run");

if (string.IsNullOrWhiteSpace(canvasUrl) || string.IsNullOrWhiteSpace(canvasToken))
{
    AnsiConsole.MarkupLine("[red]Missing Canvas:BaseUrl or Canvas:Token in user-secrets.[/]");
    return 1;
}

// 2. Header
AnsiConsole.Write(new Rule("[bold blue]CanvasTrelloSync[/]"));
if (dryRun)
    AnsiConsole.MarkupLine("[black on yellow] DRY RUN [/] Nothing will be changed on Trello.");
AnsiConsole.WriteLine();

// 3. Load courses and their assignments from Canvas
ITaskSource source = new CanvasClient(canvasUrl, canvasToken);
var assignmentsByCourse = new Dictionary<Course, List<Assignment>>();

try
{
    await AnsiConsole.Status().StartAsync("Loading from Canvas...", async _ =>
    {
        foreach (var course in await source.GetCoursesAsync())
        {
            try
            {
                assignmentsByCourse[course] = await source.GetAssignmentsAsync(course);
            }
            catch (HttpRequestException ex)
            {
                // Some courses block assignment access; skip that one and keep going
                AnsiConsole.MarkupLine($"[yellow]Skipped {Markup.Escape(course.CourseCode ?? "?")}: {Markup.Escape(ex.Message)}[/]");
            }
        }
    });
}
catch (HttpRequestException ex)
{
    AnsiConsole.Write(new Panel($"[red]{Markup.Escape(ex.Message)}[/]\nCheck Canvas:Token and Canvas:BaseUrl in user-secrets.")
        .Header("API error").BorderColor(Color.Red));
    return 1;
}

// Hide courses with no assignments
var courses = assignmentsByCourse.Where(pair => pair.Value.Count > 0).ToList();

// 4. Courses table
var courseTable = new Table().Border(TableBorder.Rounded).Title("[bold]My courses[/]");
courseTable.AddColumn("Code");
courseTable.AddColumn("Name");
courseTable.AddColumn(new TableColumn("To do").RightAligned());
courseTable.AddColumn(new TableColumn("Done").RightAligned());

foreach (var (course, assignments) in courses)
{
    int done = assignments.Count(a => a.IsSubmitted);
    courseTable.AddRow(
        Markup.Escape(course.CourseCode ?? "?"),
        Markup.Escape(course.Name ?? "(no name)"),
        $"[yellow]{assignments.Count - done}[/]",
        $"[green]{done}[/]");
}
AnsiConsole.Write(courseTable);

// 5. Assignments table: to-do first
var assignmentTable = new Table().Border(TableBorder.Rounded).Title("[bold]Assignments[/]");
assignmentTable.AddColumn("Status");
assignmentTable.AddColumn("Course");
assignmentTable.AddColumn("Assignment");

var allAssignments = courses
    .SelectMany(pair => pair.Value)
    .OrderBy(a => a.IsSubmitted)
    .ThenBy(a => a.CourseCode);

foreach (var a in allAssignments)
{
    assignmentTable.AddRow(
        a.IsSubmitted ? "[green]✅ done[/]" : "[yellow]⏳ todo[/]",
        Markup.Escape(a.CourseCode ?? "?"),
        Markup.Escape(a.Name ?? "(no name)"));
}
AnsiConsole.Write(assignmentTable);

int total = courses.Sum(pair => pair.Value.Count);
int todo = courses.Sum(pair => pair.Value.Count(a => !a.IsSubmitted));
AnsiConsole.MarkupLine($"\nTotal: [bold]{total}[/] assignments, [yellow]{todo} to do[/].");
return 0;
