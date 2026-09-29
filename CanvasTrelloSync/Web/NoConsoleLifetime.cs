using Microsoft.Extensions.Hosting;

namespace CanvasTrelloSync.Web;

// Used when the dashboard runs next to the menu: without it, the web server would
// take over Ctrl+C, and Ctrl+C would stop the server instead of quitting the app.
public class NoConsoleLifetime : IHostLifetime
{
    public Task WaitForStartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
