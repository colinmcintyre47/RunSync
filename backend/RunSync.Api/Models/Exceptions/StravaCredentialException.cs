// Models/Exceptions/StravaCredentialException.cs
// Raised when a user's own Strava API credentials are missing, malformed, or rejected by Strava.
//
// Inherits InvalidOperationException so the existing mapping in ExceptionHandlingMiddleware
// turns it into a 400 with the message intact — these are all conditions the user can fix
// themselves (register an app, re-paste the secret), so the message is safe and useful to show.
//
// Messages must stay free of secret material: never interpolate a client secret, access token,
// or refresh token into one.
//
// → Thrown by StravaCredentialService.cs and StravaService.cs
// → Surfaced to the client by ExceptionHandlingMiddleware.cs

namespace RunSync.Api.Models.Exceptions;

public class StravaCredentialException : InvalidOperationException
{
    public StravaCredentialException(string message) : base(message) { }

    public StravaCredentialException(string message, Exception innerException)
        : base(message, innerException) { }

    /// <summary>The user has not registered their Strava API application yet.</summary>
    public static StravaCredentialException NotConfigured() => new(
        "You haven't connected your Strava API application yet. Create a free app at " +
        "https://www.strava.com/settings/api and save its Client ID and Client Secret in RunSync settings.");

    /// <summary>Strava rejected the credentials during an OAuth exchange or refresh.</summary>
    public static StravaCredentialException Rejected(Exception inner) => new(
        "Strava rejected your API application credentials. Check that the Client ID and Client Secret " +
        "in RunSync settings match your app at https://www.strava.com/settings/api, and that the app's " +
        "Authorization Callback Domain is set correctly.", inner);
}
