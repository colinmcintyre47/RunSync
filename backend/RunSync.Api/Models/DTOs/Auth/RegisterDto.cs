// Models/DTOs/Auth/RegisterDto.cs
// Request body for POST /api/auth/register.
// Data annotations trigger automatic 400 validation via [ApiController].
// → Processed in AuthController.cs → Register()

using System.ComponentModel.DataAnnotations;

namespace RunSync.Api.Models.DTOs.Auth;

public class RegisterDto
{
    [Required]
    [EmailAddress]
    [MaxLength(256)]
    public string Email { get; set; } = string.Empty;

    [Required]
    [MinLength(8, ErrorMessage = "Password must be at least 8 characters.")]
    [MaxLength(128)]
    public string Password { get; set; } = string.Empty;

    [Required]
    [MinLength(2)]
    [MaxLength(64)]
    public string DisplayName { get; set; } = string.Empty;
}
