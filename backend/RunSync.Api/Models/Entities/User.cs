// Models/Entities/User.cs
// Represents a RunSync application user — this is our own user table, entirely separate
// from Strava's athlete model. A user registers with email/password and may optionally
// connect their Strava account later. The Strava connection is represented by StravaToken.
//
// → One User has at most one StravaToken (configured as one-to-one in RunSyncDbContext.cs)
// → One User has many StravaActivity records (one-to-many)
// → Passwords are BCrypt-hashed in AuthController.cs — never stored or logged as plaintext

namespace RunSync.Api.Models.Entities;

public class User
{
    public int Id { get; set; }

    public string Email { get; set; } = string.Empty;

    // BCrypt hash with work factor 12. Never expose this field in any DTO response.
    public string PasswordHash { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation property — null if the user hasn't connected Strava yet
    public StravaToken? StravaToken { get; set; }

    // The user's own Strava API application credentials — null until they register one.
    // Must exist before StravaToken can, since OAuth cannot start without it.
    public StravaAppCredential? StravaAppCredential { get; set; }

    public ICollection<StravaActivity> Activities { get; set; } = new List<StravaActivity>();

    public UserTrainingPlan? TrainingPlan { get; set; }
}
