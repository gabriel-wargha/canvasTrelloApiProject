namespace CanvasTrelloSync.Models;

// One progress bar on the dashboard: how many assignments of a course are done.
public class CourseProgress
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public int Done { get; set; }
    public int Total { get; set; }
}
