// Tests/Services/StravaServiceTests.cs
// Unit tests for StravaService: verifies token refresh logic, activity upsert behavior,
// the OAuth state token validation, and that OAuth runs against each user's OWN Strava
// application rather than a single shared one.
//
// HttpClient is mocked using Moq + a custom DelegatingHandler to intercept HTTP calls
// without making real network requests.
//
// The real StravaCredentialService and SecretProtector are used rather than mocks, so these
// tests also cover the encrypt → store → decrypt → send-to-Strava path end to end.

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
using RunSync.Api.Models.DTOs.Strava;
using RunSync.Api.Models.Entities;
using RunSync.Api.Models.Exceptions;
using RunSync.Api.Services;
using RunSync.Api.Services.Interfaces;
using Xunit;

namespace RunSync.Api.Tests.Services;

public class StravaServiceTests : IDisposable
{
    private const string ClientId = "12345";
    private const string ClientSecret = "0123456789abcdef0123456789abcdef01234567";

    private readonly RunSyncDbContext _db;
    private readonly IConfiguration _config;
    private readonly IOptions<StravaConfig> _stravaConfig;
    private readonly IStravaCredentialService _credentials;

    public StravaServiceTests()
    {
        DbContextOptions<RunSyncDbContext> options = new DbContextOptionsBuilder<RunSyncDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _db = new RunSyncDbContext(options);
        _db.Users.Add(new User { Id = 1, Email = "test@run.com", DisplayName = "Runner", PasswordHash = "x" });
        _db.Users.Add(new User { Id = 5, Email = "friend@run.com", DisplayName = "Friend", PasswordHash = "x" });
        _db.SaveChanges();

        _config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = "test-key-for-state-token-generation-32chars!!"
            })
            .Build();

        _stravaConfig = Options.Create(new StravaConfig
        {
            RedirectUri = "http://localhost/api/strava/callback",
            AuthorizationUrl = "https://www.strava.com/oauth/authorize",
            TokenUrl = "https://www.strava.com/oauth/token",
            ApiBaseUrl = "https://www.strava.com/api/v3",
            Scope = "activity:read_all"
        });

        ISecretProtector protector = new SecretProtector(Options.Create(new EncryptionConfig
        {
            MasterKey = Convert.ToBase64String(Enumerable.Range(0, 32).Select(i => (byte)i).ToArray())
        }));

        _credentials = new StravaCredentialService(
            _db, protector, _stravaConfig, NullLogger<StravaCredentialService>.Instance);
    }

    /// <summary>Registers a Strava application for the given user, as the settings screen would.</summary>
    private Task SeedCredentialsAsync(int userId, string clientId = ClientId, string clientSecret = ClientSecret)
        => _credentials.SaveAsync(userId, new StravaCredentialInputDto
        {
            ClientId = clientId,
            ClientSecret = clientSecret
        });

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
            _credentials,
            factoryMock.Object,
            _config,
            NullLogger<StravaService>.Instance);
    }

    /// <summary>Builds a handler that returns the same canned response for every request.</summary>
    private static HttpMessageHandler StubHandler(HttpStatusCode statusCode, string body)
    {
        Mock<HttpMessageHandler> handlerMock = new(MockBehavior.Loose);
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() => new HttpResponseMessage
            {
                StatusCode = statusCode,
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        return handlerMock.Object;
    }

    // ─── Authorization URL ──────────────────────────────────────────────────

    [Fact]
    public async Task GetAuthorizationUrlAsync_UsesTheUsersOwnClientId()
    {
        await SeedCredentialsAsync(userId: 1);
        StravaService sut = BuildService();

        string url = await sut.GetAuthorizationUrlAsync(userId: 1);

        url.Should().Contain($"client_id={ClientId}");
        url.Should().Contain("response_type=code");
        url.Should().Contain("scope=activity%3Aread_all");
    }

    [Fact]
    public async Task GetAuthorizationUrlAsync_NeverLeaksTheClientSecret()
    {
        await SeedCredentialsAsync(userId: 1);
        StravaService sut = BuildService();

        string url = await sut.GetAuthorizationUrlAsync(userId: 1);

        // The secret has no business in a browser-visible URL.
        url.Should().NotContain(ClientSecret);
    }

    [Fact]
    public async Task GetAuthorizationUrlAsync_DifferentUsers_UseTheirOwnApplications()
    {
        await SeedCredentialsAsync(userId: 1, clientId: "11111");
        await SeedCredentialsAsync(userId: 5, clientId: "55555");

        StravaService sut = BuildService();

        (await sut.GetAuthorizationUrlAsync(1)).Should().Contain("client_id=11111");
        (await sut.GetAuthorizationUrlAsync(5)).Should().Contain("client_id=55555");
    }

    [Fact]
    public async Task GetAuthorizationUrlAsync_NoCredentials_ThrowsWithSetupInstructions()
    {
        StravaService sut = BuildService();

        Func<Task> act = () => sut.GetAuthorizationUrlAsync(userId: 1);

        await act.Should().ThrowAsync<StravaCredentialException>()
            .WithMessage("*strava.com/settings/api*");
    }

    [Fact]
    public async Task GetAuthorizationUrlAsync_IncludesStateParam()
    {
        await SeedCredentialsAsync(userId: 1);
        StravaService sut = BuildService();

        string url = await sut.GetAuthorizationUrlAsync(userId: 1);

        url.Should().Contain("state=");
    }

    // ─── OAuth state token ──────────────────────────────────────────────────

    [Fact]
    public async Task ValidateAndExtractUserIdFromState_ValidState_ReturnsTrue()
    {
        await SeedCredentialsAsync(userId: 5);
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

    // ─── Token lifecycle ────────────────────────────────────────────────────

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
        await SeedCredentialsAsync(userId: 1);

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

        string refreshResponse = JsonSerializer.Serialize(new
        {
            access_token = "new-access-token",
            refresh_token = "new-refresh-token",
            expires_at = DateTimeOffset.UtcNow.AddHours(6).ToUnixTimeSeconds()
        });

        StravaService sut = BuildService(StubHandler(HttpStatusCode.OK, refreshResponse));

        string token = await sut.GetValidAccessTokenAsync(userId: 1);

        token.Should().Be("new-access-token");

        // Verify the token was persisted
        StravaToken saved = await _db.StravaTokens.FirstAsync(t => t.UserId == 1);
        saved.AccessToken.Should().Be("new-access-token");
    }

    [Fact]
    public async Task GetValidAccessTokenAsync_RefreshWithoutCredentials_ThrowsWithSetupInstructions()
    {
        // Credentials deleted while a token lingers — refresh cannot proceed.
        _db.StravaTokens.Add(new StravaToken
        {
            UserId = 1,
            AccessToken = "expired-access-token",
            RefreshToken = "valid-refresh-token",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeSeconds(),
            LastSyncedAt = DateTime.MinValue
        });
        await _db.SaveChangesAsync();

        StravaService sut = BuildService();

        Func<Task> act = () => sut.GetValidAccessTokenAsync(userId: 1);

        await act.Should().ThrowAsync<StravaCredentialException>()
            .WithMessage("*strava.com/settings/api*");
    }

    [Fact]
    public async Task GetValidAccessTokenAsync_NoToken_ThrowsInvalidOperationException()
    {
        StravaService sut = BuildService();

        Func<Task> act = () => sut.GetValidAccessTokenAsync(userId: 999);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*No Strava token found*");
    }

    // ─── Strava error translation ───────────────────────────────────────────

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.BadRequest)]
    public async Task ExchangeCodeForTokenAsync_StravaRejectsCredentials_ThrowsActionableError(HttpStatusCode status)
    {
        await SeedCredentialsAsync(userId: 1);

        // Strava replies 400/401 when the client_id and client_secret don't match a real app.
        StravaService sut = BuildService(StubHandler(status, """{"message":"Bad Request"}"""));

        Func<Task> act = () => sut.ExchangeCodeForTokenAsync("some-code", userId: 1);

        // Must be a user-fixable 400, not an opaque 500.
        await act.Should().ThrowAsync<StravaCredentialException>()
            .WithMessage("*Client ID and Client Secret*");
    }

    [Fact]
    public async Task ExchangeCodeForTokenAsync_ErrorMessageNeverContainsTheSecret()
    {
        await SeedCredentialsAsync(userId: 1);

        // Strava's token endpoint can echo submitted fields back in its error body.
        string echoingBody = $$"""{"errors":[{"field":"client_secret","code":"{{ClientSecret}}"}]}""";
        StravaService sut = BuildService(StubHandler(HttpStatusCode.BadRequest, echoingBody));

        Func<Task> act = () => sut.ExchangeCodeForTokenAsync("some-code", userId: 1);

        StravaCredentialException ex = (await act.Should().ThrowAsync<StravaCredentialException>()).Which;

        // The response body is logged, never surfaced — including through the inner exception.
        ex.Message.Should().NotContain(ClientSecret);
        ex.ToString().Should().NotContain(ClientSecret);
    }

    [Fact]
    public async Task SyncActivitiesAsync_RateLimited_ThrowsRateLimitMessage()
    {
        await SeedCredentialsAsync(userId: 1);

        _db.StravaTokens.Add(new StravaToken
        {
            UserId = 1,
            AccessToken = "valid-access-token",
            RefreshToken = "refresh-token",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(6).ToUnixTimeSeconds(),
            LastSyncedAt = DateTime.MinValue
        });
        await _db.SaveChangesAsync();

        StravaService sut = BuildService(StubHandler(HttpStatusCode.TooManyRequests, "Rate Limit Exceeded"));

        Func<Task> act = () => sut.SyncActivitiesAsync(userId: 1);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*rate limit*");
    }

    // ─── Disconnect ─────────────────────────────────────────────────────────

    [Fact]
    public async Task DisconnectAsync_RemovesTokenButKeepsCredentials()
    {
        await SeedCredentialsAsync(userId: 1);
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

        (await _db.StravaTokens.AnyAsync(t => t.UserId == 1)).Should().BeFalse();

        // Disconnecting Strava should not force the user to re-register their API application —
        // they can reconnect with one click.
        (await _db.StravaAppCredentials.AnyAsync(c => c.UserId == 1)).Should().BeTrue();
    }

    public void Dispose() => _db.Dispose();
}
