using CanvasTrelloSync.Models;

namespace CanvasTrelloSync.Services;

// Picks the assignment to do next in one course.
public static class NextActivity
{
    // The soonest due assignment not yet done. Without due dates: the first one in course order
    // (Canvas lists assignments in the order the course lays them out).
    public static Assignment? Pick(List<Assignment> assignments)
    {
        var todo = assignments.Where(a => !a.IsDone).ToList();

        return todo.Where(a => a.DueAt is not null).MinBy(a => a.DueAt) ?? todo.FirstOrDefault();
    }
}
