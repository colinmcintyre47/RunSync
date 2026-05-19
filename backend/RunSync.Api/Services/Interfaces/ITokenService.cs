// Services/Interfaces/ITokenService.cs
// Contract for JWT generation and claim extraction.
// Keeping this behind an interface makes TokenService easily mockable in tests.
//
// → Implemented by TokenService.cs
// → Called by AuthController.cs after successful login/registration

using System.Security.Claims;
using RunSync.Api.Models.Entities;

namespace RunSync.Api.Services.Interfaces;

public interface ITokenService
{
    // Generates a signed JWT containing userId, email, and displayName claims.
    // → See TokenService.cs → GenerateToken() for implementation
    string GenerateToken(User user);

    // Extracts the integer userId from the ClaimsPrincipal attached to an HTTP request.
    // Controllers call this to identify the authenticated user without touching the DB.
    // → See TokenService.cs → GetUserIdFromToken() for implementation
    int GetUserIdFromToken(ClaimsPrincipal user);
}
