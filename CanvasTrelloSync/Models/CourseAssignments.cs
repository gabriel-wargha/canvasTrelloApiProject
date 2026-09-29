namespace CanvasTrelloSync.Models;

// One course with its assignments.
public class CourseAssignments
{
    public Course Course { get; set; } = new();
    public List<Assignment> Assignments { get; set; } = new();
}
