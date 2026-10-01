using System.Text.Json.Serialization;

namespace CanvasTrelloSync.Models;

public class Assignment
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("html_url")] public string? Url { get; set; }
    [JsonPropertyName("due_at")] public DateTimeOffset? DueAt { get; set; }

    [JsonPropertyName("points_possible")] public double? Points { get; set; }

    [JsonPropertyName("submission")] public Submission? Submission { get; set; }

    public string? CourseCode { get; set; }
    public string? CourseName { get; set; }

    public bool IsSubmitted => Submission?.WorkflowState is "submitted" or "graded" or "pending_review";
    public string CardTitle => $"[{CourseCode}] {Name}";

    public override string ToString()
    {
        string status = IsSubmitted ? "done" : "todo";
        string due = DueAt is null ? "no due date" : DueAt.Value.ToLocalTime().ToString("ddd MM/dd h:mm tt");
        return $"[{status}] {due,-22} {CardTitle}";
    }

}