// Tests/Services/TokenServiceTests.cs
// Unit tests for TokenService: verifies JWT generation contains correct claims
// and that GetUserIdFromToken correctly extracts the userId claim.
//
// Uses a real TokenService with in-memory IConfiguration — no mocking needed here
// because the service has no external dependencies, only configuration values.

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using RunSync.Api.Models.Entities;
using RunSync.Api.Services;
using Xunit;

namespace RunSync.Api.Tests.Services;

public class TokenServiceTests
{
    private static TokenService BuildService(int expiryMinutes = 60)
    {
        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = "test-signing-key-that-is-at-least-32-chars!!",
                ["Jwt:Issuer"] = "RunSync.Tests",
                ["Jwt:Audience"] = "RunSync.Tests",
                ["Jwt:ExpiryMinutes"] = expiryMinutes.ToString()
            })
            .Build();

        return new TokenService(config, NullLogger<TokenService>.Instance);
    }

    private static User BuildUser(int id = 42, string email = "runner@example.com", string displayName = "Test Runner")
        => new() { Id = id, Email = email, DisplayName = displayName, PasswordHash = "hash" };

    [Fact]
    public void GenerateToken_ReturnsNonEmptyString()
    {
        TokenService sut = BuildService();
        string token = sut.GenerateToken(BuildUser());
        token.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void GenerateToken_ContainsUserIdClaim()
    {
        TokenService sut = BuildService();
        User user = BuildUser(id: 99);

        string token = sut.GenerateToken(user);

        JwtSecurityToken jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        string? subClaim = jwt.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Sub)?.Value;

        subClaim.Should().Be("99");
    }

    [Fact]
    public void GenerateToken_ContainsEmailClaim()
    {
        TokenService sut = BuildService();
        User user = BuildUser(email: "test@run.com");

        string token = sut.GenerateToken(user);

        JwtSecurityToken jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        string? emailClaim = jwt.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Email)?.Value;

        emailClaim.Should().Be("test@run.com");
    }

    [Fact]
    public void GenerateToken_ContainsDisplayNameClaim()
    {
        TokenService sut = BuildService();
        User user = BuildUser(displayName: "Colin Runner");

        string token = sut.GenerateToken(user);

        JwtSecurityToken jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        string? displayNameClaim = jwt.Claims.FirstOrDefault(c => c.Type == "displayName")?.Value;

        displayNameClaim.Should().Be("Colin Runner");
    }

    [Fact]
    public void GenerateToken_ExpiryMatchesConfiguration()
    {
        TokenService sut = BuildService(expiryMinutes: 30);
        User user = BuildUser();

        DateTime before = DateTime.UtcNow;
        string token = sut.GenerateToken(user);
        DateTime after = DateTime.UtcNow;

        JwtSecurityToken jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);

        jwt.ValidTo.Should().BeAfter(before.AddMinutes(29))
                            .And.BeBefore(after.AddMinutes(31));
    }

    [Fact]
    public void GetUserIdFromToken_ExtractsCorrectUserId()
    {
        TokenService sut = BuildService();
        User user = BuildUser(id: 77);
        string token = sut.GenerateToken(user);

        // Build a ClaimsPrincipal from the generated token (simulates what ASP.NET does)
        JwtSecurityToken jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        ClaimsPrincipal principal = new(new ClaimsIdentity(jwt.Claims, "Bearer"));

        int extractedId = sut.GetUserIdFromToken(principal);

        extractedId.Should().Be(77);
    }

    [Fact]
    public void GetUserIdFromToken_ThrowsWhenSubClaimMissing()
    {
        TokenService sut = BuildService();
        ClaimsPrincipal emptyPrincipal = new(new ClaimsIdentity());

        Action act = () => sut.GetUserIdFromToken(emptyPrincipal);

        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*userId claim*");
    }
}
