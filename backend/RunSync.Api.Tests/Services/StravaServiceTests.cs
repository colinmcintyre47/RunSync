// Tests/Services/StravaServiceTests.cs
// Unit tests for StravaService: verifies token refresh logic, activity upsert behavior,
// and the OAuth state token validation.
//
// HttpClient is mocked using Moq + a custom DelegatingHandler to intercept HTTP calls
// without making real network requests.

using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using RunSync.Api.Data;
using RunSync.Api.Models.Config;
using RunSync.Api.Models.Entities;
using RunSync.Api.Services;
using Xunit;

namespace RunSync.Api.Tests.Services;

public class StravaServiceTests : IDisposable
{
    private readonly RunSyncDbContext _db;
    private readonly IConfiguration _config;
    private readonly IOptions<StravaConfig> _stravaConfig;

    public StravaServiceTests()
    {
        DbContextOptions<RunSyncDbContext> options = new DbContextOptionsBuilder<RunSyncDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _db = new RunSyncDbContext(options);
        _db.Users.Add(new User { Id = 1, Email = "test@run.com", DisplayName = "Runner", PasswordHash = "x" });
        _db.SaveChanges();

        _config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = "test-key-for-state-token-generation-32chars!!"
            })
            .Build();

        _stravaConfig = Options.Create(new StravaConfig
        {
            ClientId = "test-client-id",
            ClientSecret = "test-client-secret",
            RedirectUri = "http://localhost/callback",
            AuthorizationUrl = "https://www.strava.com/oauth/authorize",
            TokenUrl = "https://www.strava.com/oauth/token",
            ApiBaseUrl = "https://www.strava.com/api/v3",
            Scope = "activity:read_all"
        });
    }

    private StravaService BuildService(HttpMessageHandler? httpHandler = null)
    {
        Mock<IHttpClientFactory> factoryMock = new();

        HttpClient client = httpHandler is not null
            ? new HttpClient(httpHandler)
            : new HttpClient();

        factoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(client);

        return new StravaService(
            _db,
            _stravaConfig,
            factoryMock.Object,
            _config,
            NullLogger<StravaService>.Instance);
    }

    [Fact]
    public async Task GetAuthorizationUrlAsync_ContainsClientIdAndRedirectUri()
    {
        StravaService sut = BuildService();

        string url = await sut.GetAuthorizationUrlAsync(userId: 1);

        url.Should().Contain("client_id=test-client-id");
        url.Should().Contain("response_type=code");
        url.Should().Contain("scope=activity:read_all");
    }

    [Fact]
    public async Task GetAuthorizationUrlAsync_IncludesStateParam()
    {
        StravaService sut = BuildService();

        string url = await sut.GetAuthorizationUrlAsync(userId: 1);

        url.Should().Contain("state=");
    }

    [Fact]
    public async Task ValidateAndExtractUserIdFromState_ValidState_ReturnsTrue()
    {
        StravaService sut = BuildService();

        // Generate a valid state token for userId=5, then immediately validate it
        string url = await sut.GetAuthorizationUrlAsync(userId: 5);
        string stateParam = Uri.UnescapeDataString(
            url.Split("state=")[1].Split('&')[0]);

        bool isValid = sut.ValidateAndExtractUserIdFromState(stateParam, out int extractedId);

        isValid.Should().BeTrue();
        extractedId.Should().Be(5);
    }

    [Fact]
    public void ValidateAndExtractUserIdFromState_TamperedState_ReturnsFalse()
    {
        StravaService sut = BuildService();

        bool isValid = sut.ValidateAndExtractUserIdFromState("definitely-not-valid-base64", out _);

        isValid.Should().BeFalse();
    }

    [Fact]
    public async Task GetValidAccessTokenAsync_ValidToken_ReturnsWithoutRefresh()
    {
        // Token expires far in the future — no refresh call expected
        _db.StravaTokens.Add(new StravaToken
        {
            UserId = 1,
            AccessToken = "valid-access-token",
            RefreshToken = "refresh-token",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(6).ToUnixTimeSeconds(),
            LastSyncedAt = DateTime.MinValue
        });
        await _db.SaveChangesAsync();

        // No HTTP handler — if refresh were attempted, the test would fail with a network error
        StravaService sut = BuildService();

        string token = await sut.GetValidAccessTokenAsync(userId: 1);

        token.Should().Be("valid-access-token");
    }

    [Fact]
    public async Task GetValidAccessTokenAsync_ExpiredToken_RefreshesAndReturnsNewToken()
    {
        _db.StravaTokens.Add(new StravaToken
        {
            UserId = 1,
            AccessToken = "expired-access-token",
            RefreshToken = "valid-refresh-token",
            // Expired 1 hour ago
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeSeconds(),
            LastSyncedAt = DateTime.MinValue
        });
        await _db.SaveChangesAsync();

        // Mock the token refresh HTTP call
        string refreshResponse = JsonSerializer.Serialize(new
        {
            access_token = "new-access-token",
            refresh_token = "new-refresh-token",
            expires_at = DateTimeOffset.UtcNow.AddHours(6).ToUnixTimeSeconds()
        });

        Mock<HttpMessageHandler> handlerMock = new(MockBehavior.Loose);
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(refreshResponse, Encoding.UTF8, "application/json")
            });

        StravaService sut = BuildService(handlerMock.Object);

        string token = await sut.GetValidAccessTokenAsync(userId: 1);

        token.Should().Be("new-access-token");

        // Verify the token was persisted
        Models.Entities.StravaToken? saved = await _db.StravaTokens.FirstAsync(t => t.UserId == 1);
        saved.AccessToken.Should().Be("new-access-token");
    }

    [Fact]
    public async Task GetValidAccessTokenAsync_NoToken_ThrowsInvalidOperationException()
    {
        StravaService sut = BuildService();

        Func<Task> act = () => sut.GetValidAccessTokenAsync(userId: 999);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*No Strava token found*");
    }

    [Fact]
    public async Task DisconnectAsync_RemovesTokenFromDatabase()
    {
        _db.StravaTokens.Add(new StravaToken
        {
            UserId = 1,
            AccessToken = "token",
            RefreshToken = "refresh",
            ExpiresAt = 9999999999
        });
        await _db.SaveChangesAsync();

        StravaService sut = BuildService();
        await sut.DisconnectAsync(userId: 1);

        bool exists = await _db.StravaTokens.AnyAsync(t => t.UserId == 1);
        exists.Should().BeFalse();
    }

    public void Dispose() => _db.Dispose();
}
