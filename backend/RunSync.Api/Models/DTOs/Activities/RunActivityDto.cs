namespace RunSync.Api.Models.DTOs.Activities;

public class RunActivityDto
{
    public long StravaId { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public float Miles { get; set; }
    public string Pace { get; set; } = string.Empty;
    public float? AvgHeartrate { get; set; }
    public float? MaxHeartrate { get; set; }
    public float ElevationGainFeet { get; set; }
    public string EffortLevel { get; set; } = string.Empty;
    public bool IsManualEntry { get; set; }

    // Enrichment fields
    public int? SufferScore { get; set; }
    public float? AverageCadence { get; set; }     // steps/min
    public string WorkoutTypeLabel { get; set; } = string.Empty; // "Race", "Long Run", "Workout", "Run"
    public string SportType { get; set; } = string.Empty;        // "Run", "TrailRun", "VirtualRun"
    public int ElapsedTimeSeconds { get; set; }
    public string ElapsedTime { get; set; } = string.Empty;      // formatted "H:MM:SS"
    public string MovingTime { get; set; } = string.Empty;       // formatted "H:MM:SS"
    public int PrCount { get; set; }
    public string SummaryPolyline { get; set; } = string.Empty;
}
