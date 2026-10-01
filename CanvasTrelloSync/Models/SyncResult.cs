namespace CanvasTrelloSync.Models;

// What one sync did (or, in a dry run, would do).
public class SyncResult
{
    public bool DryRun { get; set; }
    public List<Assignment> Created { get; } = new();
    public List<Assignment> Moved { get; } = new();
    public List<Assignment> Regrouped { get; } = new();   // old cards moved from the shared "Later" to their course list
    public int Skipped { get; set; }
    public List<string> FailedCourses { get; } = new();   // courses Canvas would not let us read
}
