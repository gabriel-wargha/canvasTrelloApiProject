namespace CanvasTrelloSync.Models;

// The Trello card we made for one Canvas assignment.
public class SyncedCard
{
    public string CardId { get; set; } = "";
    public string? CardUrl { get; set; }
    public bool Done { get; set; }                // true once the card was moved to Done
    public DateTimeOffset SyncedAt { get; set; }
}
