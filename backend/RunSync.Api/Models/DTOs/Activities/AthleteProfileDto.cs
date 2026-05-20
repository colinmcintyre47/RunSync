namespace RunSync.Api.Models.DTOs.Activities;

public class AthleteProfileDto
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string ProfileUrl { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public int StravaAthleteId { get; set; }
}
