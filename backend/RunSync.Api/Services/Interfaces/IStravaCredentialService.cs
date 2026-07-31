// Services/Interfaces/IStravaCredentialService.cs
// Contract for managing each user's own Strava API application credentials.
//
// This is the only component permitted to decrypt a stored client secret. StravaService asks
// it for a resolved credential immediately before calling Strava's token endpoint; nothing
// else in the codebase should ever hold a plaintext secret.
//
// → Implemented by StravaCredentialService.cs
// → Called by StravaController.cs (save/read/delete) and StravaService.cs (resolve)

using RunSync.Api.Models.DTOs.Strava;

namespace RunSync.Api.Services.Interfaces;

/// <summary>
/// A user's Strava application credentials, decrypted and ready to send to Strava.
/// Intentionally a short-lived record — do not cache, log, or serialise instances of this.
/// </summary>
public readonly record struct StravaCredential(string ClientId, string ClientSecret);

public interface IStravaCredentialService
{
    /// <summary>
    /// Encrypts and stores (or replaces) the user's Strava application credentials.
    /// </summary>
    Task SaveAsync(int userId, StravaCredentialInputDto input);

    /// <summary>
    /// Returns the non-sensitive status of the user's credentials for display in settings.
    /// Never includes the client secret.
    /// </summary>
    Task<StravaCredentialStatusDto> GetStatusAsync(int userId);

    /// <summary>
    /// Deletes the user's credentials and, because they can no longer be refreshed without the
    /// issuing application, any Strava tokens obtained through them.
    /// </summary>
    Task DeleteAsync(int userId);

    /// <summary>
    /// Decrypts and returns the user's credentials for an outbound Strava call.
    /// Throws <see cref="Models.Exceptions.StravaCredentialException"/> if none are configured.
    /// </summary>
    Task<StravaCredential> ResolveAsync(int userId);

    /// <summary>True if the user has saved credentials. Does not decrypt anything.</summary>
    Task<bool> ExistsAsync(int userId);
}
