// Controllers/StravaController.cs
// Manages the Strava OAuth 2.0 connection lifecycle for an authenticated RunSync user.
//
// Endpoints:
//   GET    /api/strava/credentials  → returns the user's Strava app setup status (never the secret)
//   PUT    /api/strava/credentials  → saves the user's own Client ID + Client Secret (encrypted)
//   DELETE /api/strava/credentials  → removes the credentials and any tokens they produced
//   GET    /api/strava/authorize    → returns the Strava OAuth URL for frontend redirect
//   GET    /api/strava/callback     → handles Strava's redirect back after user approves
//   POST   /api/strava/sync        → triggers a fresh activity sync from Strava
//   DELETE /api/strava/disconnect  → removes stored tokens and disconnects Strava
//
// Because a Strava application only supports a single athlete on the free tier, each user
// registers their own application and supplies its credentials via the /credentials endpoints.
// The authorize flow then runs against that user's own app.
//
// The callback endpoint does NOT require [Authorize] because Strava redirects to it before
// the frontend has a chance to attach the JWT. Instead, userId is recovered from the
// cryptographically signed "state" parameter that was embedded during GetAuthorizationUrl.
//
// → StravaService.cs handles all the actual OAuth + sync logic
// → StravaCredentialService.cs owns credential storage and encryption
// → TokenService.cs extracts userId from JWT on [Authorize] endpoints

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RunSync.Api.Models.DTOs.Strava;
using RunSync.Api.Models.Exceptions;
using RunSync.Api.Services;
using RunSync.Api.Services.Interfaces;

namespace RunSync.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class StravaController : ControllerBase
{
    private readonly IStravaService _stravaService;
    private readonly IStravaCredentialService _credentialService;
    private readonly ITokenService _tokenService;
    private readonly ILogger<StravaController> _logger;

    public StravaController(
        IStravaService stravaService,
        IStravaCredentialService credentialService,
        ITokenService tokenService,
        ILogger<StravaController> logger)
    {
        _stravaService = stravaService;
        _credentialService = credentialService;
        _tokenService = tokenService;
        _logger = logger;
    }

    // ─── Strava API application credentials ─────────────────────────────────

    /// <summary>
    /// Returns whether the user has registered their own Strava API application, along with the
    /// callback domain they must configure on Strava's side. Never returns the client secret.
    /// → See StravaCredentialService.cs → GetStatusAsync()
    /// </summary>
    [Authorize]
    [HttpGet("credentials")]
    [ProducesResponseType(typeof(StravaCredentialStatusDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCredentialStatus()
    {
        int userId = _tokenService.GetUserIdFromToken(User);
        return Ok(await _credentialService.GetStatusAsync(userId));
    }

    /// <summary>
    /// Saves (or replaces) the user's Strava application credentials. The client secret is
    /// encrypted with AES-256-GCM before it reaches the database and is never readable again
    /// through the API — replacing it means pasting a new value.
    /// → See StravaCredentialService.cs → SaveAsync()
    /// </summary>
    [Authorize]
    [HttpPut("credentials")]
    [ProducesResponseType(typeof(StravaCredentialStatusDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SaveCredentials([FromBody] StravaCredentialInputDto input)
    {
        int userId = _tokenService.GetUserIdFromToken(User);

        await _credentialService.SaveAsync(userId, input);

        // Return the fresh status so the UI can update without a second round trip.
        return Ok(await _credentialService.GetStatusAsync(userId));
    }

    /// <summary>
    /// Removes the user's Strava application credentials and any tokens issued by that
    /// application, since those tokens can no longer be refreshed. Cached activities are kept.
    /// → See StravaCredentialService.cs → DeleteAsync()
    /// </summary>
    [Authorize]
    [HttpDelete("credentials")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteCredentials()
    {
        int userId = _tokenService.GetUserIdFromToken(User);
        await _credentialService.DeleteAsync(userId);
        return NoContent();
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

        // This endpoint is reached by a browser redirect, not by fetch(), so an unhandled
        // exception here would render raw JSON in the user's tab. Credential problems are the
        // expected failure mode now that each user supplies their own app, so they are caught
        // and turned back into a normal redirect the frontend can explain.
        try
        {
            await _stravaService.ExchangeCodeForTokenAsync(code, userId);
        }
        catch (StravaCredentialException ex)
        {
            _logger.LogWarning(ex, "Strava token exchange failed for user {UserId}.", userId);
            return Redirect(GetFrontendUrl() + "?strava=invalid_credentials");
        }

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
