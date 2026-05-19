// Controllers/StravaController.cs
// Manages the Strava OAuth 2.0 connection lifecycle for an authenticated RunSync user.
//
// Endpoints:
//   GET    /api/strava/authorize    → returns the Strava OAuth URL for frontend redirect
//   GET    /api/strava/callback     → handles Strava's redirect back after user approves
//   POST   /api/strava/sync        → triggers a fresh activity sync from Strava
//   DELETE /api/strava/disconnect  → removes stored tokens and disconnects Strava
//
// The callback endpoint does NOT require [Authorize] because Strava redirects to it before
// the frontend has a chance to attach the JWT. Instead, userId is recovered from the
// cryptographically signed "state" parameter that was embedded during GetAuthorizationUrl.
//
// → StravaService.cs handles all the actual OAuth + sync logic
// → TokenService.cs extracts userId from JWT on [Authorize] endpoints

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RunSync.Api.Services;
using RunSync.Api.Services.Interfaces;

namespace RunSync.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class StravaController : ControllerBase
{
    private readonly IStravaService _stravaService;
    private readonly ITokenService _tokenService;
    private readonly ILogger<StravaController> _logger;

    public StravaController(IStravaService stravaService, ITokenService tokenService, ILogger<StravaController> logger)
    {
        _stravaService = stravaService;
        _tokenService = tokenService;
        _logger = logger;
    }

    /// <summary>
    /// Returns the Strava OAuth authorization URL for the frontend to redirect to.
    /// The URL includes a signed state token for CSRF protection.
    /// → See StravaService.cs → GetAuthorizationUrlAsync() for URL construction
    /// </summary>
    [Authorize]
    [HttpGet("authorize")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public async Task<IActionResult> Authorize()
    {
        int userId = _tokenService.GetUserIdFromToken(User);
        string authUrl = await _stravaService.GetAuthorizationUrlAsync(userId);
        return Ok(new { url = authUrl });
    }

    /// <summary>
    /// Receives the OAuth callback from Strava after the user approves authorization.
    /// Strava sends ?code=<one-time-code>&state=<our-state-token>.
    /// We validate the state token to verify this callback is legitimate, then exchange
    /// the code for persistent access + refresh tokens.
    /// On success, redirects the user back to the frontend with ?connected=true.
    /// → See StravaService.cs → ExchangeCodeForTokenAsync() for token exchange
    /// </summary>
    [HttpGet("callback")]
    public async Task<IActionResult> Callback([FromQuery] string code, [FromQuery] string state, [FromQuery] string? error)
    {
        // User denied authorization on Strava's page
        if (!string.IsNullOrEmpty(error))
        {
            _logger.LogWarning("Strava OAuth denied by user. Error: {Error}", error);
            return Redirect(GetFrontendUrl() + "?strava=denied");
        }

        // Validate the CSRF state token and extract the userId it was issued for
        if (!(_stravaService as StravaService)!.ValidateAndExtractUserIdFromState(state, out int userId))
        {
            _logger.LogWarning("Invalid or expired OAuth state token received.");
            return BadRequest(new { message = "Invalid or expired authorization request." });
        }

        await _stravaService.ExchangeCodeForTokenAsync(code, userId);

        // Redirect to frontend — the frontend will detect the query param and show a success toast
        return Redirect(GetFrontendUrl() + "?strava=connected");
    }

    /// <summary>
    /// Triggers a manual sync of Strava activities for the authenticated user.
    /// The frontend calls this when the user clicks "Sync Now".
    /// → See StravaService.cs → SyncActivitiesAsync() for sync logic
    /// </summary>
    [Authorize]
    [HttpPost("sync")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Sync()
    {
        int userId = _tokenService.GetUserIdFromToken(User);
        await _stravaService.SyncActivitiesAsync(userId);
        return NoContent();
    }

    /// <summary>
    /// Removes stored Strava tokens, disconnecting the user's Strava account from RunSync.
    /// Cached activities are retained so the user's history isn't lost.
    /// → See StravaService.cs → DisconnectAsync()
    /// </summary>
    [Authorize]
    [HttpDelete("disconnect")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Disconnect()
    {
        int userId = _tokenService.GetUserIdFromToken(User);
        await _stravaService.DisconnectAsync(userId);
        return NoContent();
    }

    // Read frontend URL from config to avoid hardcoding the Netlify URL here
    private string GetFrontendUrl()
    {
        return HttpContext.RequestServices
            .GetRequiredService<IConfiguration>()["Cors:AllowedOrigin"] ?? "/";
    }
}

/*
 * ─── WHAT CONNECTS HERE ───────────────────────────────────────────────────────
 * This file is called by:   HTTP clients (Strava's OAuth redirect + frontend)
 * This file calls into:     StravaService.cs (all Strava logic)
 *                           TokenService.cs (userId extraction from JWT)
 * Next logical file to read: ActivityService.cs
 * ─────────────────────────────────────────────────────────────────────────────
 */
