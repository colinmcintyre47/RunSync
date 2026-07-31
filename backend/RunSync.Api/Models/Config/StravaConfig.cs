// Models/Config/StravaConfig.cs
// Strongly-typed configuration object bound from the "Strava" section of appsettings.json.
// Registered in Program.cs via builder.Services.Configure<StravaConfig>(...)
// and injected into StravaService via IOptions<StravaConfig>.
//
// This holds only the values that are IDENTICAL for every user: Strava's own endpoints, the
// requested scope, and RunSync's single shared OAuth callback URL.
//
// ClientId and ClientSecret deliberately do NOT live here. Each user registers their own
// Strava API application (a free app has an athlete capacity of 1), so credentials are stored
// per user — encrypted — in StravaAppCredential and resolved via StravaCredentialService.
//
// RedirectUri is shared by design: every user's Strava application points its
// "Authorization Callback Domain" at the same RunSync host, and the signed OAuth state
// parameter identifies which user (and therefore which application) a callback belongs to.
//
// → Consumed by StravaService.cs for OAuth URL construction and API calls
// → Per-user credentials: see StravaCredentialService.cs

namespace RunSync.Api.Models.Config;

public class StravaConfig
{
    /// <summary>
    /// RunSync's own OAuth callback URL, e.g. https://api.runsync.example/api/strava/callback
    /// Shared by all users; its host is what each user enters as their app's
    /// "Authorization Callback Domain" on strava.com/settings/api.
    /// </summary>
    public string RedirectUri { get; set; } = string.Empty;

    public string AuthorizationUrl { get; set; } = string.Empty;
    public string TokenUrl { get; set; } = string.Empty;
    public string ApiBaseUrl { get; set; } = string.Empty;
    public string Scope { get; set; } = string.Empty;
}
