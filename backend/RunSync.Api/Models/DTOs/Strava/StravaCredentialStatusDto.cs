// Models/DTOs/Strava/StravaCredentialStatusDto.cs
// Response body for GET /api/strava/credentials.
//
// Note what is absent: there is no ClientSecret property, and there deliberately never will be.
// Once saved, a client secret is write-only from the API's perspective — the UI shows a masked
// placeholder and the user re-pastes the value if they need to change it. This is the same
// posture as a password field.
//
// CallbackDomain and RedirectUri are echoed back so the settings screen can tell the user
// exactly what to enter in their Strava app configuration, instead of hardcoding the deployed
// hostname into the frontend.
//
// → Produced by StravaController.cs → GetCredentialStatus()
// → Mirrored in frontend/src/types/strava.ts as StravaCredentialStatus

namespace RunSync.Api.Models.DTOs.Strava;

public class StravaCredentialStatusDto
{
    /// <summary>True once the user has saved a Client ID and Client Secret.</summary>
    public bool IsConfigured { get; set; }

    /// <summary>
    /// The saved Client ID, or null if none. Safe to return — it is public information that
    /// appears in the OAuth URL. Lets the UI show the user which app is currently wired up.
    /// </summary>
    public string? ClientId { get; set; }

    /// <summary>
    /// The exact value the user must enter as "Authorization Callback Domain" in their Strava
    /// app settings. Strava wants a bare domain here — no scheme, no path, no port.
    /// </summary>
    public string CallbackDomain { get; set; } = string.Empty;

    /// <summary>
    /// The full callback URL RunSync will send Strava to. Shown for reference/debugging;
    /// Strava itself only asks for the domain.
    /// </summary>
    public string RedirectUri { get; set; } = string.Empty;

    /// <summary>When the credentials were last saved or replaced. Null if never configured.</summary>
    public DateTime? UpdatedAt { get; set; }
}
