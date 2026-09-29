using CanvasTrelloSync.Interfaces;
using CanvasTrelloSync.Models;

namespace CanvasTrelloSync.Tests.Fakes;

// Pretend Canvas: tests put courses and assignments in, SyncService reads them out.
public class FakeTaskSource : ITaskSource
{
    private readonly Dictionary<long, List<Assignment>> _assignments = new();
    private readonly List<Course> _courses = new();

    public HashSet<long> FailingCourseIds { get; } = new();

    public Assignment AddTodo(long id, long courseId = 1)
    {
        var assignment = new Assignment { Id = id, Name = $"Assignment {id}", CourseCode = $"C{courseId}" };

        if (!_assignments.ContainsKey(courseId))
        {
            _courses.Add(new Course { Id = courseId, CourseCode = $"C{courseId}" });
            _assignments[courseId] = new List<Assignment>();
        }

        _assignments[courseId].Add(assignment);
        return assignment;
    }

    public static void Submit(Assignment assignment) =>
        assignment.Submission = new Submission { WorkflowState = "submitted" };

    public Task<List<Course>> GetCoursesAsync() => Task.FromResult(_courses.ToList());

    public Task<List<Assignment>> GetAssignmentsAsync(Course course)
    {
        if (FailingCourseIds.Contains(course.Id))
            throw new HttpRequestException("Canvas returned 403 Forbidden");

        return Task.FromResult(_assignments[course.Id].ToList());
    }
}
