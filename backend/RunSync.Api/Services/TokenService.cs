// Services/TokenService.cs
// Handles JWT generation and claim extraction for RunSync's authentication layer.
// Tokens are signed with HMAC-SHA256 using the key from appsettings Jwt:Key.
// Each token carries userId, email, and displayName as claims.
//
// → Interface: ITokenService.cs
// → Called by AuthController.cs → Register() and Login()
// → The issued token is stored by the frontend and sent as "Authorization: Bearer <token>"

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using RunSync.Api.Models.Entities;
using RunSync.Api.Services.Interfaces;

namespace RunSync.Api.Services;

public class TokenService : ITokenService
{
    // Reuse JwtSecurityTokenHandler — it's thread-safe and expensive to construct
    private static readonly JwtSecurityTokenHandler TokenHandler = new();

    private readonly IConfiguration _configuration;
    private readonly ILogger<TokenService> _logger;

    public TokenService(IConfiguration configuration, ILogger<TokenService> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Creates a signed JWT with userId, email, and displayName claims.
    /// The token is valid for Jwt:ExpiryMinutes from the time of issuance.
    /// → See AuthController.cs → Login() / Register() — this is called immediately after credential verification
    /// </summary>
    public string GenerateToken(User user)
    {
        string key = _configuration["Jwt:Key"]
            ?? throw new InvalidOperationException("JWT signing key is not configured.");

        // Key must be at least 256 bits (32 bytes) for HMAC-SHA256
        SymmetricSecurityKey securityKey = new(Encoding.UTF8.GetBytes(key));
        SigningCredentials credentials = new(securityKey, SecurityAlgorithms.HmacSha256);

        Claim[] claims =
        [
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim("displayName", user.DisplayName),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        ];

        int expiryMinutes = _configuration.GetValue<int>("Jwt:ExpiryMinutes", 60);

        JwtSecurityToken token = new(
            issuer: _configuration["Jwt:Issuer"],
            audience: _configuration["Jwt:Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(expiryMinutes),
            signingCredentials: credentials
        );

        _logger.LogDebug("Generated JWT for user {UserId}, expires in {ExpiryMinutes} minutes.", user.Id, expiryMinutes);

        return TokenHandler.WriteToken(token);
    }

    /// <summary>
    /// Extracts the userId integer from the JWT ClaimsPrincipal attached to an HTTP request.
    /// Controllers call this instead of parsing claims manually.
    /// → See StravaController.cs and ActivitiesController.cs — both call this to identify the caller
    /// </summary>
    public int GetUserIdFromToken(ClaimsPrincipal user)
    {
        string? subClaim = user.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? user.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!int.TryParse(subClaim, out int userId))
            throw new InvalidOperationException("JWT does not contain a valid userId claim.");

        return userId;
    }
}

/*
 * ─── WHAT CONNECTS HERE ───────────────────────────────────────────────────────
 * This file is called by:   AuthController.cs → Register() and Login()
 *                           StravaController.cs → GetAuthorizationUrlAsync (state param)
 * This file reads from:     IConfiguration (appsettings Jwt:Key, Jwt:Issuer, etc.)
 * Next logical file to read: AuthController.cs
 * ─────────────────────────────────────────────────────────────────────────────
 */
