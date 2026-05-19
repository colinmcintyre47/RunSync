// Models/DTOs/Strava/StravaActivityDto.cs
// Deserializes a single activity object from GET /api/v3/athlete/activities.
// Property names use snake_case to match Strava's JSON format via JsonPropertyName.
// Only maps the fields RunSync actually uses — Strava returns many more.
//
// → Deserialized in StravaService.cs → SyncActivitiesAsync()
// → Mapped to StravaActivity entity before being persisted to the database

using System.Text.Json.Serialization;

namespace RunSync.Api.Models.DTOs.Strava;

public class StravaActivityDto
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    // e.g. "Run", "Walk", "Ride" — we filter to "Run" during sync
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    // Meters
    [JsonPropertyName("distance")]
    public float Distance { get; set; }

    // Seconds
    [JsonPropertyName("moving_time")]
    public int MovingTime { get; set; }

    [JsonPropertyName("average_heartrate")]
    public float AverageHeartrate { get; set; }

    // Meters per second
    [JsonPropertyName("average_speed")]
    public float AverageSpeed { get; set; }

    // Meters
    [JsonPropertyName("total_elevation_gain")]
    public float TotalElevationGain { get; set; }

    [JsonPropertyName("start_date")]
    public DateTime StartDate { get; set; }

    [JsonPropertyName("start_date_local")]
    public DateTime StartDateLocal { get; set; }

    [JsonPropertyName("manual")]
    public bool Manual { get; set; }
}
