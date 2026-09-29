using CanvasTrelloSync.Models;

namespace CanvasTrelloSync.Interfaces;

// Anything that can give us courses and assignments (Canvas today, a fake in tests).
public interface ITaskSource
{
    Task<List<Course>> GetCoursesAsync();
    Task<List<Assignment>> GetAssignmentsAsync(Course course);
}
