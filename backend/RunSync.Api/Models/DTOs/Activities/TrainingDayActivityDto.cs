// Models/DTOs/Activities/TrainingDayActivityDto.cs
// The primary response object returned by GET /api/activities/training-plan.
// Each instance represents one day in the training plan, combining the planned workout
// with the actual Strava activity logged on that day (if any).
//
// The frontend uses this to render the "planned vs actual" view on each day card.
// Null activity fields indicate no run was logged on that day.
//
// → Assembled in ActivityService.cs → GetMatchedActivitiesAsync()
// → Consumed by frontend/src/hooks/useStravaActivities.ts
// → Rendered by frontend/src/components/TrainingDay.tsx

namespace RunSync.Api.Models.DTOs.Activities;

public class TrainingDayActivityDto
{
    // ─── Training Plan Side (always populated) ─────────────────────────────

    public DateTime PlanDate { get; set; }

    // Human-readable label, e.g. "Week 6 · Tuesday"
    public string DayLabel { get; set; } = string.Empty;

    // e.g. "Easy Run", "Tempo Run", "Long Run", "Rest"
    public string WorkoutType { get; set; } = string.Empty;

    public float PlannedMiles { get; set; }

    // e.g. "Zone 2 (aerobic)", "Zone 4 (threshold)"
    public string HeartRateZone { get; set; } = string.Empty;

    // Scientific rationale for the workout — shown as coaching notes in the UI
    public string Notes { get; set; } = string.Empty;

    // ─── Actual Strava Activity Side (null if no activity logged that day) ─

    public long? StravaActivityId { get; set; }
    public string? ActivityName { get; set; }

    // Converted from meters: DistanceMeters / 1609.34
    public float? ActualMiles { get; set; }

    // Formatted as "MM:SS /mi" — computed from MovingTimeSeconds and distance
    public string? ActualPace { get; set; }

    public float? AverageHeartrate { get; set; }

    // Converted from meters: TotalElevationGain / 0.3048
    public float? ElevationGainFeet { get; set; }

    // True when a Strava activity was matched to this plan day
    public bool IsCompleted => StravaActivityId.HasValue;
}
