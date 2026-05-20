namespace RunSync.Api.Models.DTOs.Training;

public class TrainingGoalDto
{
    public string GoalType { get; set; } = string.Empty;
    public string GoalLabel { get; set; } = string.Empty;   // "Half Marathon"
    public string RaceDate { get; set; } = string.Empty;    // ISO date
    public string FitnessLevel { get; set; } = string.Empty;
    public float CurrentWeeklyMiles { get; set; }
    public float CurrentLongRunMiles { get; set; }
    public int? GoalFinishMinutes { get; set; }
    public List<string> RunDays { get; set; } = new();
    public string LongRunDay { get; set; } = string.Empty;
    public int DaysToRace { get; set; }
    public int TotalPlanWeeks { get; set; }
}

public class TrainingPlanDto
{
    public TrainingGoalDto Goal { get; set; } = new();
    public int CurrentWeekNumber { get; set; }              // 1-based
    public double OverallCompletionRate { get; set; }       // 0.0–1.0
    public List<TrainingWeekDto> Weeks { get; set; } = new();
}

public class TrainingWeekDto
{
    public int WeekNumber { get; set; }
    public string WeekLabel { get; set; } = string.Empty;  // "Week 3 · Build"
    public string Phase { get; set; } = string.Empty;      // Base | Build | Peak | Taper
    public string WeekStart { get; set; } = string.Empty;  // YYYY-MM-DD
    public string WeekEnd { get; set; } = string.Empty;
    public float TargetMiles { get; set; }
    public bool IsCutbackWeek { get; set; }
    public bool IsCurrentWeek { get; set; }
    public int PlannedRuns { get; set; }
    public int CompletedRuns { get; set; }
    public List<TrainingDayDto> Days { get; set; } = new();
}

public class TrainingDayDto
{
    public string Date { get; set; } = string.Empty;          // YYYY-MM-DD
    public string DayOfWeek { get; set; } = string.Empty;     // "Mon" | "Tue" …
    public bool IsRunDay { get; set; }
    public string Status { get; set; } = string.Empty;        // upcoming | today | completed | missed
    public PlannedRunDto? PlannedRun { get; set; }
    public RestTipDto? RestTip { get; set; }
    public CompletedActivityDto? CompletedActivity { get; set; }
}

public class PlannedRunDto
{
    public string RunType { get; set; } = string.Empty;       // Easy Run | Tempo Run | Long Run | Intervals | Recovery
    public float TargetMiles { get; set; }
    public string EffortLevel { get; set; } = string.Empty;   // Easy | Moderate | Hard
    public int HrZone { get; set; }                           // 1–5
    public string Description { get; set; } = string.Empty;
    public string PaceGuidance { get; set; } = string.Empty;
}

public class RestTipDto
{
    public string Category { get; set; } = string.Empty;      // Nutrition | Sleep | Recovery | Mental | Strength
    public string Headline { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
}

public class CompletedActivityDto
{
    public float Miles { get; set; }
    public string Pace { get; set; } = string.Empty;
    public string ActivityName { get; set; } = string.Empty;
}
