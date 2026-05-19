// Models/DTOs/Auth/LoginDto.cs
// Request body for POST /api/auth/login.
// → Processed in AuthController.cs → Login()

using System.ComponentModel.DataAnnotations;

namespace RunSync.Api.Models.DTOs.Auth;

public class LoginDto
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string Password { get; set; } = string.Empty;
}
