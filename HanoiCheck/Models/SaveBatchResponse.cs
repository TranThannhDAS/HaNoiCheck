using System.Text.Json;
using System.Text.Json.Serialization;

namespace HanoiCheck.Models;

public sealed class SaveBatchResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("data")]
    public JsonElement? Data { get; set; }
}
