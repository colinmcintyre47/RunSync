// Models/DTOs/Strava/StravaTokenResponseDto.cs
// Deserializes the JSON response from Strava's token endpoint:
//   POST https://www.strava.com/oauth/token
// Used in both the initial code exchange and subsequent token refreshes.
// Property names use snake_case to match Strava's JSON format via JsonPropertyName.
//
// → Deserialized in StravaService.cs → ExchangeCodeForTokenAsync() and RefreshTokenAsync()
// → The values are then mapped into a StravaToken entity and persisted to the database

using System.Text.Json.Serialization;

namespace RunSync.Api.Models.DTOs.Strava;

public class StravaTokenResponseDto
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = string.Empty;

    [JsonPropertyName("refresh_token")]
    public string RefreshToken { get; set; } = string.Empty;

    // Unix timestamp in seconds — when the access token expires
    [JsonPropertyName("expires_at")]
    public long ExpiresAt { get; set; }

    [JsonPropertyName("athlete")]
    public StravaAthleteDto? Athlete { get; set; }
}

public class StravaAthleteDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("firstname")]
    public string FirstName { get; set; } = string.Empty;

    [JsonPropertyName("lastname")]
    public string LastName { get; set; } = string.Empty;

    // Full-resolution profile photo URL
    [JsonPropertyName("profile")]
    public string ProfileUrl { get; set; } = string.Empty;

    [JsonPropertyName("city")]
    public string City { get; set; } = string.Empty;

    [JsonPropertyName("state")]
    public string State { get; set; } = string.Empty;
}
