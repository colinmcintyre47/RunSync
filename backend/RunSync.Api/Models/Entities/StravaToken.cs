// Models/Entities/StravaToken.cs
// Stores the OAuth 2.0 tokens returned by Strava after a user authorizes RunSync.
// Strava access tokens expire every 6 hours. Before any API call, StravaService checks
// ExpiresAt against the current Unix timestamp and automatically refreshes if needed.
//
// → Token is created/updated by StravaService.cs → ExchangeCodeForTokenAsync()
// → Token refresh logic lives in StravaService.cs → RefreshTokenAsync()
// → GetValidAccessTokenAsync() orchestrates the expiry check before every Strava call
// → Tied to User.cs via UserId foreign key (one-to-one relationship)

namespace RunSync.Api.Models.Entities;

public class StravaToken
{
    public int Id { get; set; }

    // Foreign key → User.Id (one-to-one)
    public int UserId { get; set; }
    public User User { get; set; } = null!;

    // Short-lived token sent in Authorization header for Strava API calls
    public string AccessToken { get; set; } = string.Empty;

    // Long-lived token used to obtain a new AccessToken when it expires
    public string RefreshToken { get; set; } = string.Empty;

    // Unix timestamp (seconds since epoch) — Strava sends this directly in the token response.
    // Compare against DateTimeOffset.UtcNow.ToUnixTimeSeconds() to check expiry.
    public long ExpiresAt { get; set; }

    // Strava's internal ID for the authenticated athlete — useful for building Strava profile links
    public int StravaAthleteId { get; set; }

    // Tracks when activities were last pulled from Strava — shown in SyncStatus UI
    public DateTime LastSyncedAt { get; set; }
}
