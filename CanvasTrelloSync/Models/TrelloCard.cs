using System.Text.Json.Serialization;

namespace CanvasTrelloSync.Models;

public class TrelloCard
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("desc")] public string? Description { get; set; }
    [JsonPropertyName("idList")] public string ListId { get; set; } = "";
    [JsonPropertyName("shortUrl")] public string? Url { get; set; }
    [JsonPropertyName("due")] public DateTimeOffset? Due { get; set; }
}
