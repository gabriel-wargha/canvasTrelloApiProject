namespace CanvasTrelloSync.Models;

// Everything read from Canvas in one go.
public class CanvasSnapshot
{
    public List<CourseAssignments> Courses { get; } = new();
    public List<string> FailedCourses { get; } = new();   // courses Canvas would not let us read

    public IEnumerable<Assignment> AllAssignments => Courses.SelectMany(c => c.Assignments);
}
