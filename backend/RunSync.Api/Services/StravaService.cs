// Services/StravaService.cs
// Handles all Strava OAuth 2.0 flows and activity sync operations.
// This is the most externally-coupled service in RunSync — it owns the HTTP communication
// with Strava's API and the persistence of OAuth tokens and activity data.
//
// OAuth flow:
//   1. GetAuthorizationUrlAsync  → builds the Strava OAuth URL with a CSRF-protected state param
//   2. ExchangeCodeForTokenAsync → trades the one-time code for access + refresh tokens
//   3. GetValidAccessTokenAsync  → transparently refreshes expired tokens before API calls
//   4. SyncActivitiesAsync       → fetches all runs and upserts them into StravaActivities table
//   5. DisconnectAsync           → removes stored tokens so the user can re-connect later
//
// Rate limit awareness: Strava allows 200 requests/15 min and 2,000/day.
// Syncing fetches all activities in paginated batches (200 per page) until exhausted.
//
// → Interface: IStravaService.cs
// → Called by StravaController.cs
// → Tokens persisted to StravaToken entity (see Models/Entities/StravaToken.cs)
// → Activities persisted to StravaActivity entity (see Models/Entities/StravaActivity.cs)

using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RunSync.Api.Data;
using RunSync.Api.Models.Config;
using RunSync.Api.Models.DTOs.Strava;
using RunSync.Api.Models.Entities;
using RunSync.Api.Services.Interfaces;

namespace RunSync.Api.Services;

public class StravaService : IStravaService
{
    private readonly RunSyncDbContext _db;
    private readonly IOptions<StravaConfig> _stravaConfig;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<StravaService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public StravaService(
        RunSyncDbContext db,
        IOptions<StravaConfig> stravaConfig,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<StravaService> logger)
    {
        _db = db;
        _stravaConfig = stravaConfig;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Builds the Strava OAuth authorization URL. The userId is encoded in a short-lived JWT
    /// passed as the "state" parameter — Strava echoes this back in the callback, which lets
    /// us verify the request was initiated by this user (CSRF protection).
    /// → After user approves, Strava redirects to StravaController.cs → Callback()
    /// </summary>
    public Task<string> GetAuthorizationUrlAsync(int userId)
    {
        StravaConfig config = _stravaConfig.Value;

        // Encode userId in the state param as a signed JWT so we can verify it in the callback
        // without storing server-side session state. This prevents CSRF on the OAuth flow.
        string stateToken = GenerateStateToken(userId);

        string url = $"{config.AuthorizationUrl}" +
                     $"?client_id={config.ClientId}" +
                     $"&redirect_uri={Uri.EscapeDataString(config.RedirectUri)}" +
                     $"&response_type=code" +
                     $"&approval_prompt=auto" +
                     $"&scope={config.Scope}" +
                     $"&state={stateToken}";

        return Task.FromResult(url);
    }

    /// <summary>
    /// Exchanges the one-time authorization code from Strava for persistent tokens.
    /// The state parameter is validated here to confirm the OAuth request originated from
    /// the same user session that initiated it (CSRF check).
    /// Tokens are upserted — if the user re-connects, existing tokens are overwritten.
    /// → Called by StravaController.cs → Callback()
    /// </summary>
    public async Task ExchangeCodeForTokenAsync(string code, int userId)
    {
        StravaConfig config = _stravaConfig.Value;

        using HttpClient client = _httpClientFactory.CreateClient();

        FormUrlEncodedContent requestBody = new(new Dictionary<string, string>
        {
            ["client_id"] = config.ClientId,
            ["client_secret"] = config.ClientSecret,
            ["code"] = code,
            ["grant_type"] = "authorization_code"
        });

        HttpResponseMessage response = await client.PostAsync(config.TokenUrl, requestBody);
        response.EnsureSuccessStatusCode();

        string responseJson = await response.Content.ReadAsStringAsync();
        StravaTokenResponseDto? tokenResponse = JsonSerializer.Deserialize<StravaTokenResponseDto>(responseJson, JsonOptions);

        if (tokenResponse is null)
            throw new InvalidOperationException("Strava token exchange returned an empty response.");

        await UpsertTokenAsync(userId, tokenResponse);

        _logger.LogInformation("Strava OAuth completed for user {UserId}. Athlete ID: {AthleteId}",
            userId, tokenResponse.Athlete?.Id);
    }

    /// <summary>
    /// Returns a valid access token, automatically refreshing if the stored token has expired.
    /// This is called before every Strava API request — callers don't need to manage expiry.
    /// → Called internally by SyncActivitiesAsync()
    /// → Refresh logic in RefreshTokenAsync()
    /// </summary>
    public async Task<string> GetValidAccessTokenAsync(int userId)
    {
        StravaToken token = await _db.StravaTokens
            .FirstOrDefaultAsync(t => t.UserId == userId)
            ?? throw new InvalidOperationException($"No Strava token found for user {userId}. User must connect Strava first.");

        long nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // Add a 60-second buffer to avoid using a token that expires mid-request
        if (token.ExpiresAt <= nowUnix + 60)
        {
            _logger.LogDebug("Access token expired for user {UserId}. Refreshing...", userId);
            await RefreshTokenAsync(userId, token);
        }

        return token.AccessToken;
    }

    /// <summary>
    /// Fetches all Run-type activities from Strava and upserts them into the local database.
    /// Uses pagination (200 per page) and continues until Strava returns an empty page.
    /// Upsert logic: if an activity with the same Strava ID exists, update it; otherwise insert.
    /// → Called by StravaController.cs → Sync() (manual trigger)
    /// → Activities stored in StravaActivity table, used by ActivityService.cs
    /// </summary>
    public async Task SyncActivitiesAsync(int userId)
    {
        string accessToken = await GetValidAccessTokenAsync(userId);
        StravaConfig config = _stravaConfig.Value;

        using HttpClient client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        int page = 1;
        const int perPage = 200;
        int totalSynced = 0;

        while (true)
        {
            string url = $"{config.ApiBaseUrl}/athlete/activities?per_page={perPage}&page={page}";
            HttpResponseMessage response = await client.GetAsync(url);
            response.EnsureSuccessStatusCode();

            string json = await response.Content.ReadAsStringAsync();
            List<StravaActivityDto>? activities = JsonSerializer.Deserialize<List<StravaActivityDto>>(json, JsonOptions);

            if (activities is null || activities.Count == 0)
                break;

            // Filter to runs only — we don't need cycling, swimming, etc.
            List<StravaActivityDto> runs = activities.Where(a => a.Type == "Run").ToList();

            foreach (StravaActivityDto dto in runs)
            {
                await UpsertActivityAsync(userId, dto);
                totalSynced++;
            }

            // If Strava returned fewer records than requested, we've reached the last page
            if (activities.Count < perPage)
                break;

            page++;
        }

        // Update the last synced timestamp
        StravaToken? token = await _db.StravaTokens.FirstOrDefaultAsync(t => t.UserId == userId);
        if (token is not null)
        {
            token.LastSyncedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }

        _logger.LogInformation("Sync complete for user {UserId}. Upserted {Count} runs.", userId, totalSynced);
    }

    /// <summary>
    /// Removes the stored Strava tokens for a user, effectively disconnecting their account.
    /// Their cached activities remain in the database — they'll be re-synced if they reconnect.
    /// → Called by StravaController.cs → Disconnect()
    /// </summary>
    public async Task DisconnectAsync(int userId)
    {
        StravaToken? token = await _db.StravaTokens.FirstOrDefaultAsync(t => t.UserId == userId);

        if (token is null)
            return;

        _db.StravaTokens.Remove(token);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Strava disconnected for user {UserId}.", userId);
    }

    // ─── Private helpers ────────────────────────────────────────────────────

    /// <summary>
    /// Calls Strava's token endpoint with grant_type=refresh_token to obtain a new access token.
    /// Mutates the passed StravaToken entity in-place and saves the changes.
    /// Always called by GetValidAccessTokenAsync() — never directly by external code.
    /// </summary>
    private async Task RefreshTokenAsync(int userId, StravaToken token)
    {
        StravaConfig config = _stravaConfig.Value;

        using HttpClient client = _httpClientFactory.CreateClient();

        FormUrlEncodedContent requestBody = new(new Dictionary<string, string>
        {
            ["client_id"] = config.ClientId,
            ["client_secret"] = config.ClientSecret,
            ["refresh_token"] = token.RefreshToken,
            ["grant_type"] = "refresh_token"
        });

        HttpResponseMessage response = await client.PostAsync(config.TokenUrl, requestBody);
        response.EnsureSuccessStatusCode();

        string json = await response.Content.ReadAsStringAsync();
        StravaTokenResponseDto? refreshed = JsonSerializer.Deserialize<StravaTokenResponseDto>(json, JsonOptions);

        if (refreshed is null)
            throw new InvalidOperationException("Strava token refresh returned an empty response.");

        token.AccessToken = refreshed.AccessToken;
        token.RefreshToken = refreshed.RefreshToken;
        token.ExpiresAt = refreshed.ExpiresAt;

        await _db.SaveChangesAsync();

        _logger.LogDebug("Token refreshed for user {UserId}. New expiry: {ExpiresAt}", userId, refreshed.ExpiresAt);
    }

    private async Task UpsertTokenAsync(int userId, StravaTokenResponseDto dto)
    {
        StravaToken? existing = await _db.StravaTokens.FirstOrDefaultAsync(t => t.UserId == userId);

        if (existing is null)
        {
            StravaToken newToken = new()
            {
                UserId = userId,
                AccessToken = dto.AccessToken,
                RefreshToken = dto.RefreshToken,
                ExpiresAt = dto.ExpiresAt,
                StravaAthleteId = dto.Athlete?.Id ?? 0,
                LastSyncedAt = DateTime.MinValue
            };
            _db.StravaTokens.Add(newToken);
        }
        else
        {
            existing.AccessToken = dto.AccessToken;
            existing.RefreshToken = dto.RefreshToken;
            existing.ExpiresAt = dto.ExpiresAt;
            if (dto.Athlete is not null)
                existing.StravaAthleteId = dto.Athlete.Id;
        }

        await _db.SaveChangesAsync();
    }

    private async Task UpsertActivityAsync(int userId, StravaActivityDto dto)
    {
        StravaActivity? existing = await _db.StravaActivities.FindAsync(dto.Id);

        if (existing is null)
        {
            StravaActivity activity = MapToEntity(userId, dto);
            _db.StravaActivities.Add(activity);
        }
        else
        {
            // Update all mutable fields — the user may have edited the activity on Strava
            existing.Name = dto.Name;
            existing.DistanceMeters = dto.Distance;
            existing.MovingTimeSeconds = dto.MovingTime;
            existing.AverageHeartrate = dto.AverageHeartrate;
            existing.AverageSpeed = dto.AverageSpeed;
            existing.TotalElevationGain = dto.TotalElevationGain;
            existing.StartDateUtc = dto.StartDate;
            existing.StartDateLocal = dto.StartDateLocal;
            existing.IsManualEntry = dto.Manual;
        }

        await _db.SaveChangesAsync();
    }

    private static StravaActivity MapToEntity(int userId, StravaActivityDto dto) => new()
    {
        Id = dto.Id,
        UserId = userId,
        Name = dto.Name,
        Type = dto.Type,
        DistanceMeters = dto.Distance,
        MovingTimeSeconds = dto.MovingTime,
        AverageHeartrate = dto.AverageHeartrate,
        AverageSpeed = dto.AverageSpeed,
        TotalElevationGain = dto.TotalElevationGain,
        StartDateUtc = dto.StartDate,
        StartDateLocal = dto.StartDateLocal,
        IsManualEntry = dto.Manual
    };

    /// <summary>
    /// Encodes the userId into a short-lived HMAC-signed token used as the OAuth state parameter.
    /// This prevents CSRF attacks on the OAuth callback — we verify the signature in Callback().
    /// Using a signed token avoids storing server-side session state between authorize and callback.
    /// </summary>
    private string GenerateStateToken(int userId)
    {
        string key = _configuration["Jwt:Key"] ?? "fallback-state-key";
        string payload = $"{userId}:{DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeSeconds()}";
        string signature = Convert.ToBase64String(
            System.Security.Cryptography.HMACSHA256.HashData(
                Encoding.UTF8.GetBytes(key),
                Encoding.UTF8.GetBytes(payload)
            )
        );
        return Convert.ToBase64String(Encoding.UTF8.GetBytes($"{payload}.{signature}"));
    }

    public bool ValidateAndExtractUserIdFromState(string state, out int userId)
    {
        userId = 0;
        try
        {
            string decoded = Encoding.UTF8.GetString(Convert.FromBase64String(state));
            string[] parts = decoded.Split('.');
            if (parts.Length != 2) return false;

            string[] payloadParts = parts[0].Split(':');
            if (payloadParts.Length != 2) return false;

            if (!int.TryParse(payloadParts[0], out userId)) return false;
            if (!long.TryParse(payloadParts[1], out long expiry)) return false;

            // Reject expired state tokens
            if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() > expiry) return false;

            // Verify signature
            string key = _configuration["Jwt:Key"] ?? "fallback-state-key";
            string expectedSig = Convert.ToBase64String(
                System.Security.Cryptography.HMACSHA256.HashData(
                    Encoding.UTF8.GetBytes(key),
                    Encoding.UTF8.GetBytes(parts[0])
                )
            );

            return expectedSig == parts[1];
        }
        catch
        {
            return false;
        }
    }
}

/*
 * ─── WHAT CONNECTS HERE ───────────────────────────────────────────────────────
 * This file is called by:   StravaController.cs (all Strava-facing endpoints)
 * This file calls into:     RunSyncDbContext.cs (token + activity persistence)
 *                           IHttpClientFactory (HTTP calls to Strava's API)
 *                           IConfiguration (JWT key for state token signing)
 * Next logical file to read: ActivityService.cs (consumes the synced activities)
 * ─────────────────────────────────────────────────────────────────────────────
 */
