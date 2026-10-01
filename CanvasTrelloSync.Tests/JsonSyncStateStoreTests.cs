using CanvasTrelloSync.Models;
using CanvasTrelloSync.Services;

namespace CanvasTrelloSync.Tests;

// Runs the shared store tests on the JSON file, plus a few file-only checks.
public class JsonSyncStateStoreTests : SyncStateStoreTests, IDisposable
{
    private readonly string _tempDir;
    private readonly string _statePath;

    public JsonSyncStateStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "cts-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);
        _statePath = Path.Combine(_tempDir, "sync-state.json");
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    protected override ISyncStateStore CreateStore() => new JsonSyncStateStore(_statePath);

    [Fact]
    public async Task ResetAsync_FileExists_DeletesIt()
    {
        var store = new JsonSyncStateStore(_statePath);
        await store.SaveAsync(new SyncState());

        await store.ResetAsync();

        Assert.False(File.Exists(_statePath));
    }

    [Fact]
    public async Task ResetAsync_NoFile_DoesNotThrow()
    {
        await new JsonSyncStateStore(_statePath).ResetAsync();

        Assert.False(File.Exists(_statePath));
    }
}
