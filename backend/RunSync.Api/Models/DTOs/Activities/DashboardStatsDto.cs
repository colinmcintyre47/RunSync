namespace RunSync.Api.Models.DTOs.Activities;

public class DashboardStatsDto
{
    public float MilesThisWeek { get; set; }
    public int RunsThisWeek { get; set; }
    public float TotalMilesAllTime { get; set; }

    // Consecutive calendar weeks (Mon–Sun) with at least one run, counted back from current week
    public int WeeklyStreak { get; set; }
}
