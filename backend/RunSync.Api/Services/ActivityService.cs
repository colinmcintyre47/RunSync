// Services/ActivityService.cs
// Core business logic of RunSync: loads the static training plan and matches each day
// to any Strava activity the user logged on that date.
//
// Training plan data: embedded as a static list for v1. The plan is a 12-week
// half marathon build starting from TrainingPlan:StartDate in appsettings.json.
// In v2 this could be moved to a TrainingPlan database table for user customization.
//
// Matching logic: for each plan day, look for a StravaActivity where
// StartDateLocal.Date == PlanDate.Date. If found, map it into the DTO's "actual" fields.
//
// Unit conversions happen here (meters → miles, seconds → MM:SS pace, meters → feet)
// so the frontend receives ready-to-display values.
//
// → Interface: IActivityService.cs
// → Called by ActivitiesController.cs
// → Reads from StravaActivity table (populated by StravaService.cs → SyncActivitiesAsync)

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RunSync.Api.Data;
using RunSync.Api.Models.DTOs.Activities;
using RunSync.Api.Models.Entities;
using RunSync.Api.Services.Interfaces;

namespace RunSync.Api.Services;

public class ActivityService : IActivityService
{
    private readonly RunSyncDbContext _db;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ActivityService> _logger;

    public ActivityService(RunSyncDbContext db, IConfiguration configuration, ILogger<ActivityService> logger)
    {
        _db = db;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Returns the full 12-week training plan with actual Strava activities matched by date.
    /// Each TrainingDayActivityDto represents one plan day — the "actual" fields are null
    /// for days where no run was logged.
    /// → See StravaActivity.cs for the entity being read
    /// → See TrainingDayActivityDto.cs for the response shape
    /// </summary>
    public async Task<List<TrainingDayActivityDto>> GetMatchedActivitiesAsync(int userId)
    {
        // Load all activities for this user once — avoids N+1 queries during plan matching
        List<StravaActivity> activities = await _db.StravaActivities
            .Where(a => a.UserId == userId)
            .OrderBy(a => a.StartDateLocal)
            .ToListAsync();

        // Build a lookup by local date for O(1) matching per plan day
        Dictionary<DateOnly, StravaActivity> activityByDate = activities
            .GroupBy(a => DateOnly.FromDateTime(a.StartDateLocal))
            .ToDictionary(g => g.Key, g => g.OrderByDescending(a => a.DistanceMeters).First());

        List<TrainingPlanDay> plan = BuildTrainingPlan();

        List<TrainingDayActivityDto> result = plan.Select(planDay =>
        {
            DateOnly planDate = DateOnly.FromDateTime(planDay.Date);
            activityByDate.TryGetValue(planDate, out StravaActivity? match);

            return MapToDto(planDay, match);
        }).ToList();

        _logger.LogDebug("Matched {MatchCount}/{TotalDays} training days for user {UserId}.",
            result.Count(d => d.IsCompleted), result.Count, userId);

        return result;
    }

    /// <summary>
    /// Returns last sync timestamp, total cached activity count, and whether Strava is connected.
    /// → Called by ActivitiesController.cs → GetSyncStatus()
    /// </summary>
    public async Task<(DateTime? LastSyncedAt, int TotalActivities, bool IsConnected)> GetSyncStatusAsync(int userId)
    {
        Models.Entities.StravaToken? token = await _db.StravaTokens
            .FirstOrDefaultAsync(t => t.UserId == userId);

        int count = await _db.StravaActivities.CountAsync(a => a.UserId == userId);

        DateTime? lastSync = token?.LastSyncedAt == DateTime.MinValue ? null : token?.LastSyncedAt;

        return (lastSync, count, token is not null);
    }

    // ─── Private helpers ────────────────────────────────────────────────────

    private static TrainingDayActivityDto MapToDto(TrainingPlanDay planDay, StravaActivity? activity)
    {
        TrainingDayActivityDto dto = new()
        {
            PlanDate = planDay.Date,
            DayLabel = planDay.Label,
            WorkoutType = planDay.WorkoutType,
            PlannedMiles = planDay.PlannedMiles,
            HeartRateZone = planDay.HeartRateZone,
            Notes = planDay.Notes
        };

        if (activity is null)
            return dto;

        float miles = MetersToMiles(activity.DistanceMeters);

        dto.StravaActivityId = activity.Id;
        dto.ActivityName = activity.Name;
        dto.ActualMiles = MathF.Round(miles, 2);
        dto.ActualPace = FormatPace(activity.MovingTimeSeconds, miles);
        dto.AverageHeartrate = activity.AverageHeartrate > 0 ? activity.AverageHeartrate : null;
        dto.ElevationGainFeet = MathF.Round(activity.TotalElevationGain / 0.3048f, 0);

        return dto;
    }

    private static float MetersToMiles(float meters) => meters / 1609.34f;

    /// <summary>
    /// Computes per-mile pace as "MM:SS /mi" from total seconds and distance in miles.
    /// Returns "--:-- /mi" for edge cases (zero distance, zero time) rather than throwing.
    /// </summary>
    private static string FormatPace(int totalSeconds, float miles)
    {
        if (miles <= 0 || totalSeconds <= 0)
            return "--:-- /mi";

        int secondsPerMile = (int)(totalSeconds / miles);
        int minutes = secondsPerMile / 60;
        int seconds = secondsPerMile % 60;

        return $"{minutes}:{seconds:D2} /mi";
    }

    /// <summary>
    /// Builds a 12-week half marathon training plan starting from the configured StartDate.
    /// The plan follows a base-build-peak-taper structure grounded in 80/20 training principles:
    ///   - ~80% of runs at easy/aerobic pace (Zone 1-2)
    ///   - ~20% at threshold or race pace (Zone 3-4)
    ///   - Long run distance peaks at Week 10, then tapers into race week
    ///
    /// StartDate is read from appsettings TrainingPlan:StartDate (Monday of Week 1).
    /// Rest days and cross-training days are included for completeness but have 0 planned miles.
    /// </summary>
    private List<TrainingPlanDay> BuildTrainingPlan()
    {
        if (!DateTime.TryParse(_configuration["TrainingPlan:StartDate"], out DateTime startDate))
            startDate = new DateTime(2026, 2, 2); // Fallback to a known Monday

        List<TrainingPlanDay> plan = [];

        // ── Week 1: Base Introduction ────────────────────────────────────────
        AddWeek(plan, startDate, 1, [
            new(0, "Easy Run",       2.0f, "Zone 2 (aerobic)",    "Comfortable conversational pace. Establishes aerobic base."),
            new(1, "Rest",           0.0f, "Rest",                "Full recovery. Sleep and nutrition are training too."),
            new(2, "Easy Run",       3.0f, "Zone 2 (aerobic)",    "Keep effort controlled. Heart rate should stay below 75% max."),
            new(3, "Rest",           0.0f, "Rest",                "Active recovery — walking or light stretching is fine."),
            new(4, "Easy Run",       2.0f, "Zone 2 (aerobic)",    "Short recovery run. Focus on form over pace."),
            new(5, "Long Run",       4.0f, "Zone 1-2 (easy)",     "First long run. Slow enough to hold a full conversation throughout."),
            new(6, "Rest",           0.0f, "Rest",                "Complete rest. Let the aerobic adaptations from the week take hold.")
        ]);

        // ── Week 2: Base Building ─────────────────────────────────────────────
        AddWeek(plan, startDate, 2, [
            new(0, "Easy Run",       3.0f, "Zone 2 (aerobic)",    "Easy pace. Aerobic efficiency is built at this effort."),
            new(1, "Rest",           0.0f, "Rest",                "Rest or gentle yoga/stretching."),
            new(2, "Tempo Run",      3.0f, "Zone 3-4 (threshold)","Warm up 1 mi easy, 1 mi at comfortably hard effort, cool down 1 mi."),
            new(3, "Rest",           0.0f, "Rest",                "Recovery day after tempo. Don't rush back."),
            new(4, "Easy Run",       3.0f, "Zone 2 (aerobic)",    "Easy shake-out. Legs should feel fresh again."),
            new(5, "Long Run",       5.0f, "Zone 1-2 (easy)",     "Add 1 mile from last week. Walk breaks are fine if needed."),
            new(6, "Rest",           0.0f, "Rest",                "Full rest.")
        ]);

        // ── Week 3: Aerobic Development ───────────────────────────────────────
        AddWeek(plan, startDate, 3, [
            new(0, "Easy Run",       3.0f, "Zone 2 (aerobic)",    "Easy effort. Cadence goal: ~170 steps/min."),
            new(1, "Rest",           0.0f, "Rest",                "Rest."),
            new(2, "Intervals",      4.0f, "Zone 4 (VO2max)",     "After warm-up: 4 × 800m at hard effort (3 min recovery between). Cool down."),
            new(3, "Rest",           0.0f, "Rest",                "Full recovery after intervals — these create the most adaptation."),
            new(4, "Easy Run",       3.0f, "Zone 2 (aerobic)",    "Flush the legs out. Very easy."),
            new(5, "Long Run",       6.0f, "Zone 1-2 (easy)",     "First run over 10k. Fuel with gel at mile 4 if needed."),
            new(6, "Rest",           0.0f, "Rest",                "Rest.")
        ]);

        // ── Week 4: Recovery Week ─────────────────────────────────────────────
        AddWeek(plan, startDate, 4, [
            new(0, "Easy Run",       3.0f, "Zone 2 (aerobic)",    "Planned down week. Let the previous 3 weeks consolidate."),
            new(1, "Rest",           0.0f, "Rest",                "Rest."),
            new(2, "Easy Run",       3.0f, "Zone 2 (aerobic)",    "Keep it very easy. This is an intentional recovery week."),
            new(3, "Rest",           0.0f, "Rest",                "Rest."),
            new(4, "Easy Run",       2.0f, "Zone 2 (aerobic)",    "Short and easy."),
            new(5, "Long Run",       5.0f, "Zone 1-2 (easy)",     "Shorter long run than last week — planned step-back for recovery."),
            new(6, "Rest",           0.0f, "Rest",                "Rest.")
        ]);

        // ── Week 5: Build Phase ────────────────────────────────────────────────
        AddWeek(plan, startDate, 5, [
            new(0, "Easy Run",       4.0f, "Zone 2 (aerobic)",    "Refreshed from recovery week. Pick up volume slightly."),
            new(1, "Rest",           0.0f, "Rest",                "Rest."),
            new(2, "Tempo Run",      4.0f, "Zone 3-4 (threshold)","2 miles easy + 2 miles at tempo pace (7-8/10 effort)."),
            new(3, "Rest",           0.0f, "Rest",                "Rest."),
            new(4, "Easy Run",       3.0f, "Zone 2 (aerobic)",    "Easy run with strides: 4 × 20-second pickups at end."),
            new(5, "Long Run",       7.0f, "Zone 1-2 (easy)",     "Longest run to date. Treat this as a dress rehearsal for fueling."),
            new(6, "Rest",           0.0f, "Rest",                "Rest.")
        ]);

        // ── Week 6: Build Phase (continued) ───────────────────────────────────
        AddWeek(plan, startDate, 6, [
            new(0, "Easy Run",       4.0f, "Zone 2 (aerobic)",    "Easy miles to start the week."),
            new(1, "Rest",           0.0f, "Rest",                "Rest."),
            new(2, "Intervals",      5.0f, "Zone 4 (VO2max)",     "1 mi warm-up + 5 × 1000m at 5k effort (90 sec recovery) + 1 mi cool-down."),
            new(3, "Easy Run",       3.0f, "Zone 2 (aerobic)",    "Recovery run after hard interval session."),
            new(4, "Rest",           0.0f, "Rest",                "Rest."),
            new(5, "Long Run",       8.0f, "Zone 1-2 (easy)",     "8 miles. Practice race-day nutrition: gel every 4 miles."),
            new(6, "Rest",           0.0f, "Rest",                "Rest.")
        ]);

        // ── Week 7: Peak Build ─────────────────────────────────────────────────
        AddWeek(plan, startDate, 7, [
            new(0, "Easy Run",       4.0f, "Zone 2 (aerobic)",    "Easy aerobic run."),
            new(1, "Rest",           0.0f, "Rest",                "Rest."),
            new(2, "Tempo Run",      5.0f, "Zone 3-4 (threshold)","1.5 mi warm-up + 2 mi at half marathon goal pace + 1.5 mi cool-down."),
            new(3, "Easy Run",       3.0f, "Zone 2 (aerobic)",    "Easy recovery run."),
            new(4, "Rest",           0.0f, "Rest",                "Rest."),
            new(5, "Long Run",       9.0f, "Zone 1-2 (easy)",     "9 miles. This is the highest-stress training day yet. Trust the process."),
            new(6, "Rest",           0.0f, "Rest",                "Rest.")
        ]);

        // ── Week 8: Recovery Week ──────────────────────────────────────────────
        AddWeek(plan, startDate, 8, [
            new(0, "Easy Run",       3.0f, "Zone 2 (aerobic)",    "Step-back recovery week. Embrace the lower mileage."),
            new(1, "Rest",           0.0f, "Rest",                "Rest."),
            new(2, "Easy Run",       4.0f, "Zone 2 (aerobic)",    "Comfortable effort only."),
            new(3, "Rest",           0.0f, "Rest",                "Rest."),
            new(4, "Easy Run",       3.0f, "Zone 2 (aerobic)",    "Short and easy."),
            new(5, "Long Run",       7.0f, "Zone 1-2 (easy)",     "Shorter long run — planned step-back. Fitness is consolidating."),
            new(6, "Rest",           0.0f, "Rest",                "Rest.")
        ]);

        // ── Week 9: Peak Phase ─────────────────────────────────────────────────
        AddWeek(plan, startDate, 9, [
            new(0, "Easy Run",       5.0f, "Zone 2 (aerobic)",    "Highest easy mileage of the plan. Slow and steady."),
            new(1, "Rest",           0.0f, "Rest",                "Rest."),
            new(2, "Race Pace Run",  5.0f, "Zone 3 (race pace)",  "2 mi warm-up + 3 mi at goal half marathon pace. Get used to that effort."),
            new(3, "Easy Run",       3.0f, "Zone 2 (aerobic)",    "Easy recovery run."),
            new(4, "Rest",           0.0f, "Rest",                "Rest."),
            new(5, "Long Run",       10.0f,"Zone 1-2 (easy)",     "Peak long run — 10 miles. The hay is in the barn after today."),
            new(6, "Rest",           0.0f, "Rest",                "Rest.")
        ]);

        // ── Week 10: Sharpening ────────────────────────────────────────────────
        AddWeek(plan, startDate, 10, [
            new(0, "Easy Run",       4.0f, "Zone 2 (aerobic)",    "Easy start. Body should feel strong after week 9."),
            new(1, "Rest",           0.0f, "Rest",                "Rest."),
            new(2, "Intervals",      5.0f, "Zone 4 (VO2max)",     "Final interval session: 4 × 1 mile at 10k effort (2 min recovery)."),
            new(3, "Easy Run",       3.0f, "Zone 2 (aerobic)",    "Easy shake-out."),
            new(4, "Rest",           0.0f, "Rest",                "Rest."),
            new(5, "Long Run",       8.0f, "Zone 1-2 (easy)",     "Last double-digit training long run. You are ready."),
            new(6, "Rest",           0.0f, "Rest",                "Rest.")
        ]);

        // ── Week 11: Taper ─────────────────────────────────────────────────────
        AddWeek(plan, startDate, 11, [
            new(0, "Easy Run",       4.0f, "Zone 2 (aerobic)",    "Taper begins. Resist the urge to do more."),
            new(1, "Rest",           0.0f, "Rest",                "Rest."),
            new(2, "Tempo Run",      3.0f, "Zone 3 (race pace)",  "Short tempo to stay sharp: 1 mi easy + 1 mi at race pace + 1 mi easy."),
            new(3, "Rest",           0.0f, "Rest",                "Rest."),
            new(4, "Easy Run",       3.0f, "Zone 2 (aerobic)",    "Easy run with 4 strides."),
            new(5, "Long Run",       6.0f, "Zone 1-2 (easy)",     "Final long run. Short and easy. Trust your training."),
            new(6, "Rest",           0.0f, "Rest",                "Rest.")
        ]);

        // ── Week 12: Race Week ──────────────────────────────────────────────────
        AddWeek(plan, startDate, 12, [
            new(0, "Easy Run",       3.0f, "Zone 2 (aerobic)",    "Race week. Keep it easy. Fitness is locked in."),
            new(1, "Rest",           0.0f, "Rest",                "Rest. Focus on sleep and hydration."),
            new(2, "Easy Run",       2.0f, "Zone 2 (aerobic)",    "Short shakeout. Legs should feel springy."),
            new(3, "Rest",           0.0f, "Rest",                "Rest. Carb load begins today."),
            new(4, "Shakeout Run",   1.5f, "Zone 1 (very easy)",  "2-3 miles very easy with 4 × 20-second strides. Nothing more."),
            new(5, "Rest",           0.0f, "Rest",                "Rest. Lay out your gear. Get to bed early."),
            new(6, "Race Day",       13.1f,"Zone 3-4 (race pace)","Half Marathon Race Day. Execute your plan. Trust your training. Enjoy it.")
        ]);

        return plan;
    }

    private static void AddWeek(List<TrainingPlanDay> plan, DateTime startDate, int weekNumber,
        List<(int DayOffset, string WorkoutType, float Miles, string Zone, string Notes)> days)
    {
        DateTime weekStart = startDate.AddDays((weekNumber - 1) * 7);

        foreach ((int offset, string workoutType, float miles, string zone, string notes) in days)
        {
            DateTime date = weekStart.AddDays(offset);
            string dayName = date.DayOfWeek.ToString();
            string label = $"Week {weekNumber} · {dayName}";

            plan.Add(new TrainingPlanDay
            {
                Date = date,
                Label = label,
                WorkoutType = workoutType,
                PlannedMiles = miles,
                HeartRateZone = zone,
                Notes = notes
            });
        }
    }

    // Internal model used only within this service — not exposed via API
    private sealed class TrainingPlanDay
    {
        public DateTime Date { get; init; }
        public string Label { get; init; } = string.Empty;
        public string WorkoutType { get; init; } = string.Empty;
        public float PlannedMiles { get; init; }
        public string HeartRateZone { get; init; } = string.Empty;
        public string Notes { get; init; } = string.Empty;
    }
}

/*
 * ─── WHAT CONNECTS HERE ───────────────────────────────────────────────────────
 * This file is called by:   ActivitiesController.cs → GetTrainingPlan() and GetSyncStatus()
 * This file reads from:     RunSyncDbContext.cs (StravaActivities + StravaTokens tables)
 *                           IConfiguration (TrainingPlan:StartDate)
 * Next logical file to read: ActivitiesController.cs
 * ─────────────────────────────────────────────────────────────────────────────
 */
