using System.Text.Json.Serialization;

namespace HanoiCheck.Models;

public sealed class CaptchaData
{
    [JsonPropertyName("key")]
    public string? Key { get; set; }

    [JsonPropertyName("image")]
    public string? Image { get; set; }

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }
}

public sealed class CaptchaResponse : ApiResponse<CaptchaData>;
