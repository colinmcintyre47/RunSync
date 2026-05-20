// Controllers/ActivitiesController.cs
// Exposes the training plan data with matched Strava activities to the frontend.
// All endpoints require a valid JWT — unauthenticated requests return 401 automatically.
//
// Endpoints:
//   GET /api/activities/training-plan  → full plan with matched runs
//   GET /api/activities/sync-status    → last sync time + activity count + connection state
//
// → All business logic lives in ActivityService.cs — this controller only handles HTTP concerns
// → TrainingDayActivityDto is the response shape consumed by the frontend

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RunSync.Api.Models.DTOs.Activities;
using RunSync.Api.Services.Interfaces;

namespace RunSync.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ActivitiesController : ControllerBase
{
    private readonly IActivityService _activityService;
    private readonly ITokenService _tokenService;
    private readonly ILogger<ActivitiesController> _logger;

    public ActivitiesController(
        IActivityService activityService,
        ITokenService tokenService,
        ILogger<ActivitiesController> logger)
    {
        _activityService = activityService;
        _tokenService = tokenService;
        _logger = logger;
    }

    /// <summary>
    /// Returns all 84 training plan days (12 weeks × 7 days) with matched Strava activities.
    /// Days with no logged run have null activity fields (IsCompleted = false).
    /// → See ActivityService.cs → GetMatchedActivitiesAsync() for matching + conversion logic
    /// → See TrainingDayActivityDto.cs for the response shape
    /// </summary>
    [HttpGet("training-plan")]
    [ProducesResponseType(typeof(List<TrainingDayActivityDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTrainingPlan()
    {
        int userId = _tokenService.GetUserIdFromToken(User);
        List<TrainingDayActivityDto> plan = await _activityService.GetMatchedActivitiesAsync(userId);
        return Ok(plan);
    }

    [HttpGet("sync-status")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSyncStatus()
    {
        int userId = _tokenService.GetUserIdFromToken(User);
        (DateTime? lastSyncedAt, int totalActivities, bool isConnected) = await _activityService.GetSyncStatusAsync(userId);

        return Ok(new { lastSyncedAt, totalActivities, isConnected });
    }

    /// <summary>
    /// Returns actual Strava runs grouped into rolling calendar weeks (Mon–Sun), newest first.
    /// Replaces the fixed 12-week plan view — works for any user regardless of their training goal.
    /// → See ActivityService.cs → GetWeeklyLogAsync()
    /// </summary>
    [HttpGet("weekly-log")]
    [ProducesResponseType(typeof(List<WeekSummaryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetWeeklyLog()
    {
        int userId = _tokenService.GetUserIdFromToken(User);
        List<WeekSummaryDto> weeks = await _activityService.GetWeeklyLogAsync(userId);
        return Ok(weeks);
    }

    /// <summary>
    /// Returns the four dashboard stat values: miles this week, runs this week,
    /// all-time miles, and weekly streak.
    /// → See ActivityService.cs → GetDashboardStatsAsync()
    /// </summary>
    [HttpGet("dashboard-stats")]
    [ProducesResponseType(typeof(DashboardStatsDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDashboardStats()
    {
        int userId = _tokenService.GetUserIdFromToken(User);
        DashboardStatsDto stats = await _activityService.GetDashboardStatsAsync(userId);
        return Ok(stats);
    }

    /// <summary>
    /// Returns the Strava athlete profile stored during OAuth.
    /// Returns 204 if the user hasn't connected Strava yet or profile data hasn't been synced.
    /// </summary>
    [HttpGet("athlete-profile")]
    [ProducesResponseType(typeof(AthleteProfileDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> GetAthleteProfile()
    {
        int userId = _tokenService.GetUserIdFromToken(User);
        AthleteProfileDto? profile = await _activityService.GetAthleteProfileAsync(userId);
        return profile is null ? NoContent() : Ok(profile);
    }
}

/*
 * ─── WHAT CONNECTS HERE ───────────────────────────────────────────────────────
 * This file is called by:   frontend/src/api/stravaApi.ts (getTrainingPlan, getSyncStatus)
 * This file calls into:     ActivityService.cs
 *                           TokenService.cs (userId extraction)
 * Next logical file to read: Program.cs (how all of this is wired together)
 * ─────────────────────────────────────────────────────────────────────────────
 */
