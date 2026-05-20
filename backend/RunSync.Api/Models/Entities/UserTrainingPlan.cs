namespace RunSync.Api.Models.Entities;

public class UserTrainingPlan
{
    public int Id { get; set; }

    // One-to-one with User
    public int UserId { get; set; }
    public User User { get; set; } = null!;

    // "FiveK" | "TenK" | "HalfMarathon" | "Marathon"
    public string GoalType { get; set; } = string.Empty;

    public DateTime RaceDate { get; set; }

    // "Beginner" | "Intermediate" | "Advanced"
    public string FitnessLevel { get; set; } = string.Empty;

    public float CurrentWeeklyMiles { get; set; }
    public float CurrentLongRunMiles { get; set; }

    // Target finish time in minutes (optional)
    public int? GoalFinishMinutes { get; set; }

    // JSON array of day abbreviations, e.g. ["Mon","Wed","Fri","Sun"]
    public string RunDays { get; set; } = "[]";

    // "Sat" | "Sun"
    public string LongRunDay { get; set; } = "Sun";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
