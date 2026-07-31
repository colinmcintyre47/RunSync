// Models/DTOs/Strava/StravaCredentialInputDto.cs
// Request body for PUT /api/strava/credentials — the user pasting in the Client ID and
// Client Secret from their own Strava API application.
//
// This is the ONLY place a client secret ever travels into the system. It is encrypted
// before it touches the database and is never echoed back out.
//
// → Consumed by StravaController.cs → SaveCredentials()
// → Persisted (encrypted) by StravaCredentialService.cs

using System.ComponentModel.DataAnnotations;

namespace RunSync.Api.Models.DTOs.Strava;

public class StravaCredentialInputDto
{
    /// <summary>
    /// Strava application Client ID — a short numeric string shown on strava.com/settings/api.
    /// </summary>
    [Required(ErrorMessage = "Client ID is required.")]
    [RegularExpression(@"^\d{1,20}$", ErrorMessage = "Client ID should be the numeric ID shown on your Strava API settings page.")]
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// Strava application Client Secret — a 40-character lowercase hex string.
    /// Validated by shape only; the real check is whether Strava accepts it during OAuth.
    /// </summary>
    [Required(ErrorMessage = "Client Secret is required.")]
    [RegularExpression("^[0-9a-fA-F]{40}$", ErrorMessage = "Client Secret should be the 40-character value shown on your Strava API settings page.")]
    public string ClientSecret { get; set; } = string.Empty;
}
