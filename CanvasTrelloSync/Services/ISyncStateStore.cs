using CanvasTrelloSync.Models;

namespace CanvasTrelloSync.Services;

// Where the sync state lives. JSON file today, SQLite in Block 8; SyncService doesn't care which.
public interface ISyncStateStore
{
    Task<SyncState> LoadAsync();
    Task SaveAsync(SyncState state);
}
