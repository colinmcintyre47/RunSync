// Services/Interfaces/IActivityService.cs
// Contract for fetching training plan data with matched Strava activities.
//
// → Implemented by ActivityService.cs
// → Called by ActivitiesController.cs

using RunSync.Api.Models.DTOs.Activities;

namespace RunSync.Api.Services.Interfaces;

public interface IActivityService
{
    Task<List<TrainingDayActivityDto>> GetMatchedActivitiesAsync(int userId);
    Task<(DateTime? LastSyncedAt, int TotalActivities, bool IsConnected)> GetSyncStatusAsync(int userId);

    Task<List<WeekSummaryDto>> GetWeeklyLogAsync(int userId);
    Task<DashboardStatsDto> GetDashboardStatsAsync(int userId);
    Task<AthleteProfileDto?> GetAthleteProfileAsync(int userId);
}
