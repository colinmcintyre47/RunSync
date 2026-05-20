// Models/Entities/StravaActivity.cs
// A cached copy of a Strava activity stored in our database. We cache rather than calling
// Strava on every page load to stay within Strava's rate limits (200 requests/15 min,
// 2,000/day). Activities are synced on demand via POST /api/strava/sync.
//
// Distance is stored in meters (Strava's native unit) and converted to miles in the DTO layer.
// Pace is computed in ActivityService.cs from MovingTimeSeconds and distance.
//
// → This table is populated by StravaService.cs → SyncActivitiesAsync()
// → Matched to training plan days by ActivityService.cs → GetMatchedActivitiesAsync()
// → The Strava activity ID is used as the primary key — no separate auto-increment needed

namespace RunSync.Api.Models.Entities;

public class StravaActivity
{
    // Strava's own activity ID — used as PK so upserts are idempotent
    public long Id { get; set; }

    // Foreign key → User.Id
    public int UserId { get; set; }
    public User User { get; set; } = null!;

    // e.g. "Afternoon Run", "Morning 5k" — user-defined name from Strava
    public string Name { get; set; } = string.Empty;

    // Activity type string from Strava: "Run", "Walk", "Ride", etc.
    // We filter to "Run" only during sync.
    public string Type { get; set; } = string.Empty;

    // Meters — divide by 1609.34 to convert to miles
    public float DistanceMeters { get; set; }

    // Seconds — used with distance to compute per-mile pace
    public int MovingTimeSeconds { get; set; }

    // BPM from heart rate monitor, if available (0 if no HR data)
    public float AverageHeartrate { get; set; }

    // Meters per second — multiply by 2.23694 for mph
    public float AverageSpeed { get; set; }

    // Meters — divide by 0.3048 to convert to feet
    public float TotalElevationGain { get; set; }

    // UTC timestamp from Strava — indexed in DB for fast date-range queries
    public DateTime StartDateUtc { get; set; }

    // Local time at the athlete's location — used for matching to training plan days
    // because training plans are date-based in local time, not UTC
    public DateTime StartDateLocal { get; set; }

    public bool IsManualEntry { get; set; }

    // ── Enrichment fields added in v2 ─────────────────────────────────────────

    // Strava's proprietary training load metric (0–500+). Null if HR data unavailable.
    public int? SufferScore { get; set; }

    // Steps per minute. 0 if not recorded.
    public float AverageCadence { get; set; }

    // 0=default run, 1=race, 2=long run, 3=workout
    public int WorkoutType { get; set; }

    // Peak heart rate during the activity. 0 if no HR monitor.
    public float MaxHeartrate { get; set; }

    // More granular than Type: "Run", "TrailRun", "VirtualRun", "Treadmill", etc.
    public string SportType { get; set; } = string.Empty;

    // Total wall-clock time including stops (MovingTimeSeconds excludes stops)
    public int ElapsedTimeSeconds { get; set; }

    // Number of segment PRs set during this activity
    public int PrCount { get; set; }

    // Google-encoded polyline of the route. Empty for treadmill/manual entries.
    public string SummaryPolyline { get; set; } = string.Empty;
}
