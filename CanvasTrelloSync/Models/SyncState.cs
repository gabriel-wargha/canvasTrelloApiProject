namespace CanvasTrelloSync.Models;

// Everything the app remembers between syncs.
public class SyncState
{
    public const int MaxHistory = 50;

    public Dictionary<long, SyncedCard> Cards { get; set; } = new();   // assignment id -> its card
    public List<SyncRun> History { get; set; } = new();                // oldest first
}
