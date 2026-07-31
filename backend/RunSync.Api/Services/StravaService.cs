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
// Rate limit awareness: Strava allows 200 requests/15 min and 2,000/day. Because every user
// authenticates against their OWN Strava application (see StravaAppCredential.cs), these limits
// apply per user rather than being shared across all of RunSync.
//
// Credential sourcing: the client_id and client_secret are resolved per user from
// StravaCredentialService. Only the non-secret endpoints (authorize/token/API base URLs and the
// shared redirect URI) come from StravaConfig, because those are identical for every user.
//
// → Interface: IStravaService.cs
// → Called by StravaController.cs
// → Per-user credentials from StravaCredentialService.cs
// → Tokens persisted to StravaToken entity (see Models/Entities/StravaToken.cs)
// → Activities persisted to StravaActivity entity (see Models/Entities/StravaActivity.cs)

using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RunSync.Api.Data;
using RunSync.Api.Models.Config;
using RunSync.Api.Models.DTOs.Strava;
using RunSync.Api.Models.Entities;
using RunSync.Api.Models.Exceptions;
using RunSync.Api.Services.Interfaces;

namespace RunSync.Api.Services;

public class StravaService : IStravaService
{
    private readonly RunSyncDbContext _db;
    private readonly IOptions<StravaConfig> _stravaConfig;
    private readonly IStravaCredentialService _credentials;
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
        IStravaCredentialService credentials,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<StravaService> logger)
    {
        _db = db;
        _stravaConfig = stravaConfig;
        _credentials = credentials;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Builds the Strava OAuth authorization URL against the user's OWN Strava application.
    /// The userId is encoded in a signed state parameter — Strava echoes this back in the
    /// callback, which lets us verify the request was initiated by this user (CSRF protection)
    /// and look up whose client secret to use for the code exchange.
    /// Throws if the user hasn't registered their Strava application yet.
    /// → After user approves, Strava redirects to StravaController.cs → Callback()
    /// </summary>
    public async Task<string> GetAuthorizationUrlAsync(int userId)
    {
        StravaConfig config = _stravaConfig.Value;

        // Only the client_id is needed here — the secret never appears in a browser-visible URL.
        StravaCredential credential = await _credentials.ResolveAsync(userId);

        // Encode userId in the state param as a signed token so we can verify it in the callback
        // without storing server-side session state. This prevents CSRF on the OAuth flow.
        string stateToken = GenerateStateToken(userId);

        return $"{config.AuthorizationUrl}" +
               $"?client_id={Uri.EscapeDataString(credential.ClientId)}" +
               $"&redirect_uri={Uri.EscapeDataString(config.RedirectUri)}" +
               $"&response_type=code" +
               $"&approval_prompt=auto" +
               $"&scope={Uri.EscapeDataString(config.Scope)}" +
               $"&state={Uri.EscapeDataString(stateToken)}";
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
        StravaCredential credential = await _credentials.ResolveAsync(userId);

        using HttpClient client = _httpClientFactory.CreateClient();

        using FormUrlEncodedContent requestBody = new(new Dictionary<string, string>
        {
            ["client_id"] = credential.ClientId,
            ["client_secret"] = credential.ClientSecret,
            ["code"] = code,
            ["grant_type"] = "authorization_code"
        });

        using HttpResponseMessage response = await client.PostAsync(config.TokenUrl, requestBody);
        await EnsureStravaSuccessAsync(response, userId, "token exchange");

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
            using HttpResponseMessage response = await client.GetAsync(url);
            await EnsureStravaSuccessAsync(response, userId, "activity fetch");

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
    /// Translates a failed Strava response into an exception the user can act on.
    ///
    /// Since every user supplies their own API application, a 400/401 here almost always means
    /// their Client ID/Secret is wrong or their app's callback domain doesn't match — conditions
    /// they can fix themselves. Bare EnsureSuccessStatusCode() would surface those as an opaque
    /// 500, so they are mapped to StravaCredentialException (→ HTTP 400) instead.
    ///
    /// The Strava response body is logged but never returned to the client: on the token
    /// endpoint it can echo back submitted credential fields.
    /// </summary>
    private async Task EnsureStravaSuccessAsync(HttpResponseMessage response, int userId, string operation)
    {
        if (response.IsSuccessStatusCode)
            return;

        string body = await response.Content.ReadAsStringAsync();

        _logger.LogWarning(
            "Strava {Operation} failed for user {UserId}. Status: {StatusCode}. Body: {Body}",
            operation, userId, (int)response.StatusCode, body);

        HttpRequestException inner = new(
            $"Strava {operation} returned {(int)response.StatusCode}.", null, response.StatusCode);

        // Explicitly typed as Exception: the arms return three unrelated exception types, which
        // leaves a switch expression with no best common type.
        Exception failure = response.StatusCode switch
        {
            HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized
                => StravaCredentialException.Rejected(inner),

            HttpStatusCode.TooManyRequests => new InvalidOperationException(
                "Strava's rate limit for your API application has been reached. " +
                "Limits reset every 15 minutes — please try again shortly."),

            // Anything else is an upstream problem → mapped to 502 by ExceptionHandlingMiddleware.
            _ => inner
        };

        throw failure;
    }

    /// <summary>
    /// Calls Strava's token endpoint with grant_type=refresh_token to obtain a new access token.
    /// Mutates the passed StravaToken entity in-place and saves the changes.
    /// Always called by GetValidAccessTokenAsync() — never directly by external code.
    /// </summary>
    private async Task RefreshTokenAsync(int userId, StravaToken token)
    {
        StravaConfig config = _stravaConfig.Value;
        StravaCredential credential = await _credentials.ResolveAsync(userId);

        using HttpClient client = _httpClientFactory.CreateClient();

        using FormUrlEncodedContent requestBody = new(new Dictionary<string, string>
        {
            ["client_id"] = credential.ClientId,
            ["client_secret"] = credential.ClientSecret,
            ["refresh_token"] = token.RefreshToken,
            ["grant_type"] = "refresh_token"
        });

        using HttpResponseMessage response = await client.PostAsync(config.TokenUrl, requestBody);
        await EnsureStravaSuccessAsync(response, userId, "token refresh");

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
                UserId          = userId,
                AccessToken     = dto.AccessToken,
                RefreshToken    = dto.RefreshToken,
                ExpiresAt       = dto.ExpiresAt,
                StravaAthleteId = dto.Athlete?.Id ?? 0,
                LastSyncedAt    = DateTime.MinValue,
            };
            ApplyAthleteProfile(newToken, dto.Athlete);
            _db.StravaTokens.Add(newToken);
        }
        else
        {
            existing.AccessToken  = dto.AccessToken;
            existing.RefreshToken = dto.RefreshToken;
            existing.ExpiresAt    = dto.ExpiresAt;
            if (dto.Athlete is not null)
            {
                existing.StravaAthleteId = dto.Athlete.Id;
                ApplyAthleteProfile(existing, dto.Athlete);
            }
        }

        await _db.SaveChangesAsync();
    }

    private static void ApplyAthleteProfile(StravaToken token, StravaAthleteDto? athlete)
    {
        if (athlete is null) return;
        token.AthleteFirstName  = athlete.FirstName;
        token.AthleteLastName   = athlete.LastName;
        token.AthleteProfileUrl = athlete.ProfileUrl;
        token.AthleteCity       = athlete.City;
        token.AthleteState      = athlete.State;
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
            existing.Name               = dto.Name;
            existing.DistanceMeters     = dto.Distance;
            existing.MovingTimeSeconds  = dto.MovingTime;
            existing.AverageHeartrate   = dto.AverageHeartrate;
            existing.AverageSpeed       = dto.AverageSpeed;
            existing.TotalElevationGain = dto.TotalElevationGain;
            existing.StartDateUtc       = dto.StartDate;
            existing.StartDateLocal     = dto.StartDateLocal;
            existing.IsManualEntry      = dto.Manual;
            existing.SufferScore        = dto.SufferScore;
            existing.AverageCadence     = dto.AverageCadence;
            existing.WorkoutType        = dto.WorkoutType ?? 0;
            existing.MaxHeartrate       = dto.MaxHeartrate;
            existing.SportType          = dto.SportType;
            existing.ElapsedTimeSeconds = dto.ElapsedTime;
            existing.PrCount            = dto.PrCount;
            existing.SummaryPolyline    = dto.Map?.SummaryPolyline ?? string.Empty;
        }

        await _db.SaveChangesAsync();
    }

    private static StravaActivity MapToEntity(int userId, StravaActivityDto dto) => new()
    {
        Id                  = dto.Id,
        UserId              = userId,
        Name                = dto.Name,
        Type                = dto.Type,
        DistanceMeters      = dto.Distance,
        MovingTimeSeconds   = dto.MovingTime,
        AverageHeartrate    = dto.AverageHeartrate,
        AverageSpeed        = dto.AverageSpeed,
        TotalElevationGain  = dto.TotalElevationGain,
        StartDateUtc        = dto.StartDate,
        StartDateLocal      = dto.StartDateLocal,
        IsManualEntry       = dto.Manual,
        SufferScore         = dto.SufferScore,
        AverageCadence      = dto.AverageCadence,
        WorkoutType         = dto.WorkoutType ?? 0,
        MaxHeartrate        = dto.MaxHeartrate,
        SportType           = dto.SportType,
        ElapsedTimeSeconds  = dto.ElapsedTime,
        PrCount             = dto.PrCount,
        SummaryPolyline     = dto.Map?.SummaryPolyline ?? string.Empty,
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
