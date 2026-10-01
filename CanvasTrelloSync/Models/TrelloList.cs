using System.Text.Json.Serialization;

namespace CanvasTrelloSync.Models;

public class TrelloList
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("pos")] public double Pos { get; set; }   // left-to-right order on the board
}
