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

    public int StravaAthleteId { get; set; }
    public DateTime LastSyncedAt { get; set; }

    // Athlete profile — populated during OAuth and kept in sync
    public string AthleteFirstName { get; set; } = string.Empty;
    public string AthleteLastName { get; set; } = string.Empty;
    public string AthleteProfileUrl { get; set; } = string.Empty;
    public string AthleteCity { get; set; } = string.Empty;
    public string AthleteState { get; set; } = string.Empty;
}
