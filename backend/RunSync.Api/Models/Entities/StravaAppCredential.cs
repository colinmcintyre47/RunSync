// Models/Entities/StravaAppCredential.cs
// Holds the Strava API application credentials that a user registered themselves.
//
// Why this exists: a Strava API application starts in "Single Player Mode" with an athlete
// capacity of 1. Rather than every RunSync user sharing one application (which would cap the
// whole product at a handful of athletes), each user registers their own free Strava app and
// supplies its credentials here. Every user is then the sole athlete on their own app, and
// RunSync stays free at any number of users.
//
// This is a SEPARATE table from StravaToken, not extra columns on it, because credentials are
// needed BEFORE OAuth can start, whereas a StravaToken row only exists after OAuth completes.
//
// ClientId is public information (it appears in the OAuth URL) and is stored as-is.
// ClientSecret is NEVER stored in plaintext — see ClientSecretEncrypted below.
//
// → Written/read by StravaCredentialService.cs
// → Consumed by StravaService.cs for the OAuth URL, code exchange, and token refresh
// → Tied to User.cs via UserId foreign key (one-to-one), cascade delete

namespace RunSync.Api.Models.Entities;

public class StravaAppCredential
{
    public int Id { get; set; }

    // Foreign key → User.Id (one-to-one)
    public int UserId { get; set; }
    public User User { get; set; } = null!;

    /// <summary>
    /// The Strava application's client ID. Public by design — it is visible in the OAuth
    /// authorization URL — so it is stored in plaintext and may be shown back to the user.
    /// </summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// The Strava application's client secret, encrypted with AES-256-GCM by SecretProtector.
    /// Stored as a versioned "v1.BASE64(...)" payload, never as plaintext.
    ///
    /// This value must never be returned by any API endpoint, written to a log, or included
    /// in a DTO. It is decrypted only in-process, immediately before a call to Strava's token
    /// endpoint. Combined with a refresh token it grants full access to the user's Strava
    /// account, so it is treated with the same care as a password hash.
    /// </summary>
    public string ClientSecretEncrypted { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
