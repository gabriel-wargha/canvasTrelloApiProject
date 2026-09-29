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

    // trigger is "terminal" or "web", for the history
    public async Task<SyncResult> SyncAsync(bool dryRun, string trigger)
    {
        await _lock.WaitAsync();
        try
        {
            return await RunSyncAsync(dryRun, trigger);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<SyncResult> RunSyncAsync(bool dryRun, string trigger)
    {
        var result = new SyncResult { DryRun = dryRun };
        var state = await _store.LoadAsync();
        var assignments = await GetAllAssignmentsAsync(result);

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
                    if (dryRun)
                        continue;

                    var created = await _board.CreateCardAsync(laterId, assignment);
                    state.Cards[assignment.Id] = new SyncedCard
                    {
                        CardId = created.Id,
                        CardUrl = created.Url,
                        SyncedAt = DateTimeOffset.Now,
                    };
                }
                else if (card is { Done: false } && assignment.IsSubmitted)
                {
                    // Rule 2: submitted and its card is still open -> move it to Done
                    result.Moved.Add(assignment);
                    if (dryRun)
                        continue;

                    doneId ??= await _board.EnsureListAsync(DoneList);
                    await _board.MoveCardAsync(card.CardId, doneId);
                    card.Done = true;
                    card.SyncedAt = DateTimeOffset.Now;
                }
                else
                {
                    // Rule 3: everything else (already has a card, or submitted before it ever got one)
                    result.Skipped++;
                }
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

    private async Task<List<Assignment>> GetAllAssignmentsAsync(SyncResult result)
    {
        var all = new List<Assignment>();

        foreach (var course in await _source.GetCoursesAsync())
        {
            try
            {
                all.AddRange(await _source.GetAssignmentsAsync(course));
            }
            catch (HttpRequestException)
            {
                // Some Canvas Network courses block assignment access; skip that course and keep going
                result.FailedCourses.Add(course.CourseCode ?? course.Id.ToString());
            }
        }

        return all;
    }
}
