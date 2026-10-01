using CanvasTrelloSync.Models;

namespace CanvasTrelloSync.Services;

// Where the sync state lives: a JSON file or a SQLite database. SyncService doesn't care which.
public interface ISyncStateStore
{
    Task<SyncState> LoadAsync();
    Task SaveAsync(SyncState state);
    Task ResetAsync();                 // forget every card and all history
}
