using CanvasTrelloSync.Interfaces;
using CanvasTrelloSync.Models;

namespace CanvasTrelloSync.Services;

// The sync engine: compares Canvas with what we remember, then creates or moves Trello cards.
public class SyncService
{
    public const string LaterList = "Later";   // the old shared list; new cards go to a list per course (CourseLists)
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

    public Task<List<Course>> GetCoursesAsync() => _source.GetCoursesAsync();

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

        // Cards still in the old shared "Later" list (made before each course had its own list).
        // Only reading here, so this is safe in a dry run too.
        var lists = await _board.GetListsAsync();
        var oldLaterCardIds = lists.TryGetValue(LaterList, out string? oldLaterId)
            ? (await _board.GetCardsAsync(oldLaterId)).Select(c => c.Id).ToHashSet()
            : new HashSet<string>();

        // Found only when the first card needs them, and never in a dry run (EnsureListAsync may create the list)
        string? doneId = null;
        var courseListIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);   // list name -> list id

        async Task<string> CourseListIdAsync(Assignment assignment)
        {
            string name = CourseLists.ListName(assignment.CourseName, assignment.CourseCode);
            if (!courseListIds.TryGetValue(name, out string? id))
                courseListIds[name] = id = await _board.EnsureListAsync(name);
            return id;
        }

        try
        {
            foreach (var assignment in assignments)
            {
                bool hasCard = state.Cards.TryGetValue(assignment.Id, out SyncedCard? card);

                if (!hasCard)
                {
                    // Rule 1: no card yet -> new card in its course's Later list, or straight in Done if it was already submitted
                    result.Created.Add(assignment);
                    if (!dryRun)
                    {
                        string listId = assignment.IsSubmitted
                            ? doneId ??= await _board.EnsureListAsync(DoneList)
                            : await CourseListIdAsync(assignment);
                        var created = await _board.CreateCardAsync(listId, assignment);
                        state.Cards[assignment.Id] = new SyncedCard
                        {
                            CardId = created.Id,
                            CardUrl = created.Url,
                            Done = assignment.IsSubmitted,
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
                else if (card is { Done: false } && oldLaterCardIds.Contains(card.CardId))
                {
                    // Rule 3: still to do, but its card is in the old shared Later list -> move it to its course list
                    result.Regrouped.Add(assignment);
                    if (!dryRun)
                    {
                        await _board.MoveCardAsync(card.CardId, await CourseListIdAsync(assignment));
                        card.SyncedAt = DateTimeOffset.Now;
                    }
                }
                else
                {
                    // Rule 4: everything else (its card is already in the right list)
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
