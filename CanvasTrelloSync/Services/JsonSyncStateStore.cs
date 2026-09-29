using System.Text.Json;
using CanvasTrelloSync.Models;

namespace CanvasTrelloSync.Services;

// Keeps the sync state in one JSON file (sync-state.json).
public class JsonSyncStateStore : ISyncStateStore
{
    private static readonly JsonSerializerOptions _options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,   // readable if I open the file myself
    };

    private readonly string _path;

    public JsonSyncStateStore(string path)
    {
        _path = path;
    }

    public async Task<SyncState> LoadAsync()
    {
        // No file yet means nothing has been synced
        if (!File.Exists(_path))
            return new SyncState();

        await using var stream = File.OpenRead(_path);
        return await JsonSerializer.DeserializeAsync<SyncState>(stream, _options) ?? new SyncState();
    }

    public async Task SaveAsync(SyncState state)
    {
        // Write a temp file, then swap it in, so a crash mid-write can't leave a half-written state file
        string tempPath = _path + ".tmp";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, state, _options);
        }
        File.Move(tempPath, _path, overwrite: true);
    }
}
