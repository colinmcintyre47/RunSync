// Models/Config/StravaConfig.cs
// Strongly-typed configuration object bound from the "Strava" section of appsettings.json.
// Registered in Program.cs via builder.Services.Configure<StravaConfig>(...)
// and injected into StravaService via IOptions<StravaConfig>.
//
// All values are empty strings in appsettings.json — real values come from
// appsettings.Development.json (local dev) or AWS Secrets Manager (production).
//
// → Consumed by StravaService.cs for OAuth URL construction and token exchange

namespace RunSync.Api.Models.Config;

public class StravaConfig
{
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string RedirectUri { get; set; } = string.Empty;
    public string AuthorizationUrl { get; set; } = string.Empty;
    public string TokenUrl { get; set; } = string.Empty;
    public string ApiBaseUrl { get; set; } = string.Empty;
    public string Scope { get; set; } = string.Empty;
}
