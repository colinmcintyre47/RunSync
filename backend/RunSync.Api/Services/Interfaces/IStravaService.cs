// Services/Interfaces/IStravaService.cs
// Contract for all Strava OAuth and activity sync operations.
// Each method maps to a distinct phase of the Strava integration lifecycle:
//   1. GetAuthorizationUrlAsync   — send user to Strava to authorize
//   2. ExchangeCodeForTokenAsync  — trade one-time code for persistent tokens
//   3. GetValidAccessTokenAsync   — always returns a non-expired token (auto-refreshes)
//   4. SyncActivitiesAsync        — fetch recent runs and upsert into local DB
//   5. DisconnectAsync            — remove stored tokens
//
// → Implemented by StravaService.cs
// → Called by StravaController.cs

namespace RunSync.Api.Services.Interfaces;

public interface IStravaService
{
    Task<string> GetAuthorizationUrlAsync(int userId);
    Task ExchangeCodeForTokenAsync(string code, int userId);
    Task<string> GetValidAccessTokenAsync(int userId);
    Task SyncActivitiesAsync(int userId);
    Task DisconnectAsync(int userId);
}
