using CanvasTrelloSync.Interfaces;
using CanvasTrelloSync.Models;

namespace CanvasTrelloSync.Services;

// The sync engine: compares Canvas with what we remember, then creates or moves Trello cards.
public class SyncService
{
    public const string LaterList = "Later";
    public const string DoneList = "Done";

    private readonly ITaskSource _source;
    private readonly ITaskBoard _board;
    private readonly ISyncStateStore _store;

    // Only one sync at a time (terminal and web share this service), so no card is created twice
    private readonly SemaphoreSlim _lock = new(1, 1);

    public SyncService(ITaskSource source, ITaskBoard board, ISyncStateStore store)
    {
        _source = source;
        _board = board;
        _store = store;
    }

    // trigger is "terminal" or "web", for the history.
    // onProgress(done, total) is called once Canvas is loaded and after each assignment, for a progress bar.
    public async Task<SyncResult> SyncAsync(bool dryRun, string trigger, Action<int, int>? onProgress = null)
    {
        await _lock.WaitAsync();
        try
        {
            return await RunSyncAsync(dryRun, trigger, onProgress);
        }
        finally
        {
            _lock.Release();
        }
    }

    public Task<SyncState> GetStateAsync() => _store.LoadAsync();

    // Locked too, so a reset can't happen in the middle of a sync
    public async Task ResetAsync()
    {
        await _lock.WaitAsync();
        try
        {
            await _store.ResetAsync();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<CanvasSnapshot> LoadCanvasAsync()
    {
        var snapshot = new CanvasSnapshot();

        foreach (var course in await _source.GetCoursesAsync())
        {
            try
            {
                var assignments = await _source.GetAssignmentsAsync(course);
                snapshot.Courses.Add(new CourseAssignments { Course = course, Assignments = assignments });
            }
            catch (HttpRequestException)
            {
                // Some Canvas Network courses block assignment access; skip that course and keep going
                snapshot.FailedCourses.Add(course.CourseCode ?? course.Id.ToString());
            }
        }

        return snapshot;
    }

    private async Task<SyncResult> RunSyncAsync(bool dryRun, string trigger, Action<int, int>? onProgress)
    {
        var result = new SyncResult { DryRun = dryRun };
        var state = await _store.LoadAsync();

        var canvas = await LoadCanvasAsync();
        result.FailedCourses.AddRange(canvas.FailedCourses);
        var assignments = canvas.AllAssignments.ToList();
        int handled = 0;
        onProgress?.Invoke(0, assignments.Count);

        var lists = await _board.GetListsAsync();
        if (!lists.TryGetValue(LaterList, out string? laterId))
            throw new InvalidOperationException($"The Trello board has no list named \"{LaterList}\".");

        // Found only when the first card needs to move, and never in a dry run (EnsureListAsync may create the list)
        string? doneId = null;

        try
        {
            foreach (var assignment in assignments)
            {
                bool hasCard = state.Cards.TryGetValue(assignment.Id, out SyncedCard? card);

                if (!hasCard && !assignment.IsSubmitted)
                {
                    // Rule 1: new and not submitted -> new card in Later
                    result.Created.Add(assignment);
                    if (!dryRun)
                    {
                        var created = await _board.CreateCardAsync(laterId, assignment);
                        state.Cards[assignment.Id] = new SyncedCard
                        {
                            CardId = created.Id,
                            CardUrl = created.Url,
                            SyncedAt = DateTimeOffset.Now,
                        };
                    }
                }
                else if (card is { Done: false } && assignment.IsSubmitted)
                {
                    // Rule 2: submitted and its card is still open -> move it to Done
                    result.Moved.Add(assignment);
                    if (!dryRun)
                    {
                        doneId ??= await _board.EnsureListAsync(DoneList);
                        await _board.MoveCardAsync(card.CardId, doneId);
                        card.Done = true;
                        card.SyncedAt = DateTimeOffset.Now;
                    }
                }
                else
                {
                    // Rule 3: everything else (already has a card, or submitted before it ever got one)
                    result.Skipped++;
                }

                onProgress?.Invoke(++handled, assignments.Count);
            }
        }
        catch
        {
            // Trello failed halfway: still save the cards already made, or the next sync would duplicate them
            if (!dryRun)
                await _store.SaveAsync(state);
            throw;
        }

        // A dry run is only a preview: nothing is saved
        if (dryRun)
            return result;

        state.History.Add(new SyncRun
        {
            RanAt = DateTimeOffset.Now,
            Trigger = trigger,
            Created = result.Created.Count,
            Moved = result.Moved.Count,
            Skipped = result.Skipped,
        });
        if (state.History.Count > SyncState.MaxHistory)
            state.History.RemoveRange(0, state.History.Count - SyncState.MaxHistory);

        await _store.SaveAsync(state);
        return result;
    }
}
