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

    /// <summary>
    /// Returns the sync status: when activities were last fetched, how many are cached,
    /// and whether the user's Strava account is currently connected.
    /// The frontend uses this to render the SyncStatus component.
    /// → See ActivityService.cs → GetSyncStatusAsync()
    /// </summary>
    [HttpGet("sync-status")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSyncStatus()
    {
        int userId = _tokenService.GetUserIdFromToken(User);
        (DateTime? lastSyncedAt, int totalActivities, bool isConnected) = await _activityService.GetSyncStatusAsync(userId);

        return Ok(new
        {
            lastSyncedAt,
            totalActivities,
            isConnected
        });
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
