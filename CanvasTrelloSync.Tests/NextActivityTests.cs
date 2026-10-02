using CanvasTrelloSync.Models;
using CanvasTrelloSync.Services;

namespace CanvasTrelloSync.Tests;

public class NextActivityTests
{
    private static Assignment Todo(long id, DateTimeOffset? due = null) => new() { Id = id, DueAt = due };

    private static Assignment Submitted(long id)
    {
        var assignment = Todo(id);
        assignment.Submission = new Submission { WorkflowState = "submitted" };
        return assignment;
    }

    [Fact]
    public void Pick_NoDueDates_ReturnsFirstNotSubmittedInCourseOrder()
    {
        var assignments = new List<Assignment> { Submitted(1), Todo(2), Todo(3) };

        Assert.Equal(2, NextActivity.Pick(assignments)?.Id);
    }

    [Fact]
    public void Pick_SomeDueDates_ReturnsSoonestDue()
    {
        var now = new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);
        var assignments = new List<Assignment> { Todo(1), Todo(2, now.AddDays(5)), Todo(3, now.AddDays(2)) };

        Assert.Equal(3, NextActivity.Pick(assignments)?.Id);
    }

    [Fact]
    public void Pick_SoonestDueIsSubmitted_SkipsIt()
    {
        var now = new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);
        var done = Submitted(1);
        done.DueAt = now.AddDays(1);
        var assignments = new List<Assignment> { done, Todo(2, now.AddDays(3)) };

        Assert.Equal(2, NextActivity.Pick(assignments)?.Id);
    }

    [Fact]
    public void Pick_FirstIsMarkedDone_SkipsIt()
    {
        var assignments = new List<Assignment> { Todo(1), Todo(2) };
        assignments[0].MarkedDone = true;

        Assert.Equal(2, NextActivity.Pick(assignments)?.Id);
    }

    [Fact]
    public void Pick_EverythingSubmitted_ReturnsNull()
    {
        Assert.Null(NextActivity.Pick(new List<Assignment> { Submitted(1), Submitted(2) }));
    }
}
