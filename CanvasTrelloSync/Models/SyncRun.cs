namespace CanvasTrelloSync.Models;

// One row of sync history.
public class SyncRun
{
    public DateTimeOffset RanAt { get; set; }
    public string Trigger { get; set; } = "";     // "terminal" or "web"
    public int Created { get; set; }
    public int Moved { get; set; }
    public int Skipped { get; set; }
}
