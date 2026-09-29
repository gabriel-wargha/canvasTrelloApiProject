using System.Text.Json.Serialization;

namespace CanvasTrelloSync.Models;

public class TrelloBoard
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
}
