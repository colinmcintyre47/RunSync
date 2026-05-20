namespace RunSync.Api.Models.DTOs.Activities;

public class WeekSummaryDto
{
    // e.g. "Week of May 19"
    public string WeekLabel { get; set; } = string.Empty;
    public DateOnly WeekStart { get; set; }
    public DateOnly WeekEnd { get; set; }
    public float TotalMiles { get; set; }
    public int RunCount { get; set; }
    public List<RunActivityDto> Runs { get; set; } = [];
}
