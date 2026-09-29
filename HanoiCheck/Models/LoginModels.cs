using System.Text.Json.Serialization;

namespace HanoiCheck.Models;

public sealed class LoginRequest
{
    [JsonPropertyName("username")]
    public required string Username { get; init; }

    [JsonPropertyName("password")]
    public required string Password { get; init; }

    [JsonPropertyName("captcha_key")]
    public required string CaptchaKey { get; init; }

    [JsonPropertyName("captcha_code")]
    public required string CaptchaCode { get; init; }
}

public sealed class LoginData
{
    [JsonPropertyName("access_token")]
    public string? AccessToken { get; set; }

    [JsonPropertyName("token_type")]
    public string? TokenType { get; set; }

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }

    [JsonPropertyName("refresh_token")]
    public string? RefreshToken { get; set; }

    [JsonPropertyName("refresh_expires_in")]
    public int RefreshExpiresIn { get; set; }
}

public sealed class LoginResponse : ApiResponse<LoginData>;
