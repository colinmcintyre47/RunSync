using System.ComponentModel.DataAnnotations;

namespace RunSync.Api.Models.DTOs.Training;

public class TrainingGoalInputDto
{
    [Required]
    public string GoalType { get; set; } = string.Empty;      // FiveK | TenK | HalfMarathon | Marathon

    [Required]
    public DateTime RaceDate { get; set; }

    [Required]
    public string FitnessLevel { get; set; } = string.Empty;  // Beginner | Intermediate | Advanced

    [Range(0, 150)]
    public float CurrentWeeklyMiles { get; set; }

    [Range(0, 40)]
    public float CurrentLongRunMiles { get; set; }

    public int? GoalFinishMinutes { get; set; }

    [Required, MinLength(1)]
    public List<string> RunDays { get; set; } = new();        // ["Mon","Wed","Fri","Sun"]

    [Required]
    public string LongRunDay { get; set; } = "Sun";           // Sat | Sun
}
