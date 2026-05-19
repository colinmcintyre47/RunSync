// Services/Interfaces/IActivityService.cs
// Contract for fetching training plan data with matched Strava activities.
//
// → Implemented by ActivityService.cs
// → Called by ActivitiesController.cs

using RunSync.Api.Models.DTOs.Activities;

namespace RunSync.Api.Services.Interfaces;

public interface IActivityService
{
    // Returns the full training plan with actual Strava activities matched by date.
    // → See ActivityService.cs → GetMatchedActivitiesAsync() for matching logic
    Task<List<TrainingDayActivityDto>> GetMatchedActivitiesAsync(int userId);

    // Returns the timestamp of the most recent sync and the total cached activity count.
    // → See ActivityService.cs → GetSyncStatusAsync() for implementation
    Task<(DateTime? LastSyncedAt, int TotalActivities, bool IsConnected)> GetSyncStatusAsync(int userId);
}
