// Services/StravaCredentialService.cs
// Owns the lifecycle of each user's self-registered Strava API application credentials:
// saving them encrypted, reporting their status, deleting them, and decrypting them
// on demand for StravaService.
//
// Security posture:
//   - The client secret is encrypted with AES-256-GCM before it reaches the database.
//   - The AAD context binds the ciphertext to the owning user id, so a row copied to another
//     user fails to decrypt rather than silently working.
//   - Decryption happens only inside ResolveAsync(), only for outbound Strava token calls.
//   - No method returns, logs, or otherwise exposes the plaintext secret.
//
// → Interface: IStravaCredentialService.cs
// → Encryption: SecretProtector.cs
// → Called by StravaController.cs (settings endpoints) and StravaService.cs (OAuth calls)

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RunSync.Api.Data;
using RunSync.Api.Models.Config;
using RunSync.Api.Models.DTOs.Strava;
using RunSync.Api.Models.Entities;
using RunSync.Api.Models.Exceptions;
using RunSync.Api.Services.Interfaces;
using System.Security.Cryptography;

namespace RunSync.Api.Services;

public class StravaCredentialService : IStravaCredentialService
{
    private readonly RunSyncDbContext _db;
    private readonly ISecretProtector _protector;
    private readonly IOptions<StravaConfig> _stravaConfig;
    private readonly ILogger<StravaCredentialService> _logger;

    public StravaCredentialService(
        RunSyncDbContext db,
        ISecretProtector protector,
        IOptions<StravaConfig> stravaConfig,
        ILogger<StravaCredentialService> logger)
    {
        _db = db;
        _protector = protector;
        _stravaConfig = stravaConfig;
        _logger = logger;
    }

    /// <summary>
    /// Upserts the user's credentials. Replacing a Client ID points RunSync at a different
    /// Strava application, which invalidates any tokens issued by the old one — so existing
    /// tokens are dropped and the user is asked to reconnect.
    /// → Called by StravaController.cs → SaveCredentials()
    /// </summary>
    public async Task SaveAsync(int userId, StravaCredentialInputDto input)
    {
        string clientId = input.ClientId.Trim();
        string clientSecret = input.ClientSecret.Trim();

        string encrypted = _protector.Protect(clientSecret, BuildContext(userId));

        StravaAppCredential? existing = await _db.StravaAppCredentials
            .FirstOrDefaultAsync(c => c.UserId == userId);

        bool applicationChanged = existing is not null && existing.ClientId != clientId;

        if (existing is null)
        {
            _db.StravaAppCredentials.Add(new StravaAppCredential
            {
                UserId                = userId,
                ClientId              = clientId,
                ClientSecretEncrypted = encrypted,
                CreatedAt             = DateTime.UtcNow,
                UpdatedAt             = DateTime.UtcNow,
            });
        }
        else
        {
            existing.ClientId              = clientId;
            existing.ClientSecretEncrypted = encrypted;
            existing.UpdatedAt             = DateTime.UtcNow;
        }

        // Tokens are issued by a specific Strava application. If the user points RunSync at a
        // different app, the old refresh token can never be refreshed again — remove it so the
        // UI shows "not connected" instead of failing on the next sync.
        if (applicationChanged)
        {
            StravaToken? token = await _db.StravaTokens.FirstOrDefaultAsync(t => t.UserId == userId);
            if (token is not null)
            {
                _db.StravaTokens.Remove(token);
                _logger.LogInformation(
                    "User {UserId} changed Strava application. Existing tokens removed; reconnect required.", userId);
            }
        }

        await _db.SaveChangesAsync();

        // Log the fact, never the material.
        _logger.LogInformation("Strava API credentials saved for user {UserId}.", userId);
    }

    /// <summary>
    /// Returns display-safe credential status plus the callback values the user must enter in
    /// their Strava app settings. Never touches the encrypted secret.
    /// → Called by StravaController.cs → GetCredentialStatus()
    /// </summary>
    public async Task<StravaCredentialStatusDto> GetStatusAsync(int userId)
    {
        StravaAppCredential? credential = await _db.StravaAppCredentials
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.UserId == userId);

        string redirectUri = _stravaConfig.Value.RedirectUri;

        return new StravaCredentialStatusDto
        {
            IsConfigured   = credential is not null,
            ClientId       = credential?.ClientId,
            UpdatedAt      = credential?.UpdatedAt,
            RedirectUri    = redirectUri,
            CallbackDomain = ExtractCallbackDomain(redirectUri),
        };
    }

    /// <summary>
    /// Removes the user's credentials along with any tokens those credentials produced.
    /// Cached activities are retained, matching the behaviour of a plain Strava disconnect.
    /// → Called by StravaController.cs → DeleteCredentials()
    /// </summary>
    public async Task DeleteAsync(int userId)
    {
        StravaAppCredential? credential = await _db.StravaAppCredentials
            .FirstOrDefaultAsync(c => c.UserId == userId);

        StravaToken? token = await _db.StravaTokens.FirstOrDefaultAsync(t => t.UserId == userId);

        if (credential is null && token is null)
            return;

        if (credential is not null)
            _db.StravaAppCredentials.Remove(credential);

        // Without the application's client secret these tokens can never be refreshed.
        if (token is not null)
            _db.StravaTokens.Remove(token);

        await _db.SaveChangesAsync();

        _logger.LogInformation("Strava API credentials removed for user {UserId}.", userId);
    }

    /// <summary>
    /// Decrypts the user's credentials for an outbound call to Strava.
    /// This is the single decryption point in the application.
    /// → Called by StravaService.cs before every OAuth or refresh request
    /// </summary>
    public async Task<StravaCredential> ResolveAsync(int userId)
    {
        StravaAppCredential credential = await _db.StravaAppCredentials
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.UserId == userId)
            ?? throw StravaCredentialException.NotConfigured();

        string clientSecret;
        try
        {
            clientSecret = _protector.Unprotect(credential.ClientSecretEncrypted, BuildContext(userId));
        }
        catch (CryptographicException ex)
        {
            // Almost always a key-rotation mistake on the operator's side. Log the detail for
            // CloudWatch; tell the user only that they need to re-enter the secret.
            _logger.LogError(ex, "Failed to decrypt Strava client secret for user {UserId}.", userId);

            throw new StravaCredentialException(
                "Your stored Strava Client Secret could not be read. Please re-enter it in RunSync settings.", ex);
        }

        return new StravaCredential(credential.ClientId, clientSecret);
    }

    public Task<bool> ExistsAsync(int userId)
        => _db.StravaAppCredentials.AnyAsync(c => c.UserId == userId);

    // ─── Private helpers ────────────────────────────────────────────────────

    /// <summary>
    /// Builds the AAD string bound into each ciphertext. Changing this format invalidates every
    /// stored secret, so it is versioned; a change would require a re-encryption migration.
    /// </summary>
    private static string BuildContext(int userId) => $"runsync:strava-client-secret:v1:{userId}";

    /// <summary>
    /// Reduces the configured redirect URI to the bare host Strava expects in the
    /// "Authorization Callback Domain" field (no scheme, no port, no path).
    /// </summary>
    private static string ExtractCallbackDomain(string redirectUri)
    {
        if (string.IsNullOrWhiteSpace(redirectUri))
            return string.Empty;

        return Uri.TryCreate(redirectUri, UriKind.Absolute, out Uri? uri)
            ? uri.Host
            : string.Empty;
    }
}

/*
 * ─── WHAT CONNECTS HERE ───────────────────────────────────────────────────────
 * This file is called by:   StravaController.cs (credential settings endpoints)
 *                           StravaService.cs (resolves credentials for Strava calls)
 * This file calls into:     SecretProtector.cs (AES-256-GCM encrypt/decrypt)
 *                           RunSyncDbContext.cs (StravaAppCredentials persistence)
 * Next logical file to read: StravaService.cs
 * ─────────────────────────────────────────────────────────────────────────────
 */
