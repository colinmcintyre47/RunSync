// Tests/Services/StravaCredentialServiceTests.cs
// Unit tests for StravaCredentialService — storage, encryption-at-rest, and lifecycle of each
// user's own Strava API application credentials.
//
// These tests deliberately use the REAL SecretProtector rather than a mock: the property that
// matters most is that a plaintext client secret never reaches the database, and only the real
// implementation can demonstrate that end to end.

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RunSync.Api.Data;
using RunSync.Api.Models.Config;
using RunSync.Api.Models.DTOs.Strava;
using RunSync.Api.Models.Entities;
using RunSync.Api.Models.Exceptions;
using RunSync.Api.Services;
using RunSync.Api.Services.Interfaces;
using Xunit;

namespace RunSync.Api.Tests.Services;

public class StravaCredentialServiceTests : IDisposable
{
    private const string ClientId = "123456";
    private const string ClientSecret = "0123456789abcdef0123456789abcdef01234567";

    private readonly RunSyncDbContext _db;
    private readonly StravaCredentialService _sut;

    public StravaCredentialServiceTests()
    {
        DbContextOptions<RunSyncDbContext> options = new DbContextOptionsBuilder<RunSyncDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _db = new RunSyncDbContext(options);
        _db.Users.Add(new User { Id = 1, Email = "test@run.com", DisplayName = "Runner", PasswordHash = "x" });
        _db.Users.Add(new User { Id = 2, Email = "friend@run.com", DisplayName = "Friend", PasswordHash = "x" });
        _db.SaveChanges();

        ISecretProtector protector = new SecretProtector(Options.Create(new EncryptionConfig
        {
            MasterKey = Convert.ToBase64String(Enumerable.Range(0, 32).Select(i => (byte)i).ToArray())
        }));

        IOptions<StravaConfig> stravaConfig = Options.Create(new StravaConfig
        {
            RedirectUri = "https://api.runsync.example/api/strava/callback",
            AuthorizationUrl = "https://www.strava.com/oauth/authorize",
            TokenUrl = "https://www.strava.com/oauth/token",
            ApiBaseUrl = "https://www.strava.com/api/v3",
            Scope = "activity:read_all"
        });

        _sut = new StravaCredentialService(
            _db,
            protector,
            stravaConfig,
            NullLogger<StravaCredentialService>.Instance);
    }

    private static StravaCredentialInputDto Input(string clientId = ClientId, string clientSecret = ClientSecret)
        => new() { ClientId = clientId, ClientSecret = clientSecret };

    [Fact]
    public async Task SaveAsync_DoesNotStoreSecretInPlaintext()
    {
        await _sut.SaveAsync(userId: 1, Input());

        StravaAppCredential stored = await _db.StravaAppCredentials.FirstAsync(c => c.UserId == 1);

        stored.ClientSecretEncrypted.Should().NotContain(ClientSecret);
        stored.ClientSecretEncrypted.Should().StartWith("v1.");
        // The client ID is public and intentionally stored as-is.
        stored.ClientId.Should().Be(ClientId);
    }

    [Fact]
    public async Task SaveAsync_ThenResolveAsync_ReturnsOriginalCredentials()
    {
        await _sut.SaveAsync(userId: 1, Input());

        StravaCredential resolved = await _sut.ResolveAsync(userId: 1);

        resolved.ClientId.Should().Be(ClientId);
        resolved.ClientSecret.Should().Be(ClientSecret);
    }

    [Fact]
    public async Task SaveAsync_TrimsWhitespaceFromPastedValues()
    {
        // Copy-pasting from the Strava settings page routinely drags in whitespace.
        await _sut.SaveAsync(userId: 1, Input($"  {ClientId}  ", $"  {ClientSecret}\n"));

        StravaCredential resolved = await _sut.ResolveAsync(userId: 1);

        resolved.ClientId.Should().Be(ClientId);
        resolved.ClientSecret.Should().Be(ClientSecret);
    }

    [Fact]
    public async Task SaveAsync_CalledTwice_UpdatesRatherThanDuplicates()
    {
        await _sut.SaveAsync(userId: 1, Input());
        await _sut.SaveAsync(userId: 1, Input(clientSecret: "ffffffffffffffffffffffffffffffffffffffff"));

        (await _db.StravaAppCredentials.CountAsync(c => c.UserId == 1)).Should().Be(1);
        (await _sut.ResolveAsync(userId: 1)).ClientSecret
            .Should().Be("ffffffffffffffffffffffffffffffffffffffff");
    }

    [Fact]
    public async Task SaveAsync_SameApplication_KeepsExistingTokens()
    {
        await _sut.SaveAsync(userId: 1, Input());
        await AddTokenAsync(userId: 1);

        // Rotating only the secret on the SAME app leaves existing tokens valid.
        await _sut.SaveAsync(userId: 1, Input(clientSecret: "ffffffffffffffffffffffffffffffffffffffff"));

        (await _db.StravaTokens.AnyAsync(t => t.UserId == 1)).Should().BeTrue();
    }

    [Fact]
    public async Task SaveAsync_DifferentApplication_RemovesStaleTokens()
    {
        await _sut.SaveAsync(userId: 1, Input());
        await AddTokenAsync(userId: 1);

        // Pointing at a DIFFERENT Strava app invalidates tokens the old app issued — they could
        // never be refreshed, so they must not linger and make the UI claim "connected".
        await _sut.SaveAsync(userId: 1, Input(clientId: "999999"));

        (await _db.StravaTokens.AnyAsync(t => t.UserId == 1)).Should().BeFalse();
    }

    [Fact]
    public async Task SaveAsync_IsScopedToOneUser()
    {
        await _sut.SaveAsync(userId: 1, Input());
        await _sut.SaveAsync(userId: 2, Input(clientId: "654321", clientSecret: "abcdefabcdefabcdefabcdefabcdefabcdefabcd"));

        (await _sut.ResolveAsync(1)).ClientSecret.Should().Be(ClientSecret);
        (await _sut.ResolveAsync(2)).ClientSecret.Should().Be("abcdefabcdefabcdefabcdefabcdefabcdefabcd");
    }

    [Fact]
    public async Task ResolveAsync_CiphertextMovedToAnotherUser_Fails()
    {
        await _sut.SaveAsync(userId: 1, Input());

        StravaAppCredential userOne = await _db.StravaAppCredentials.FirstAsync(c => c.UserId == 1);

        // Simulate an attacker (or a bad migration) copying user 1's encrypted secret onto
        // user 2's row. The AAD context is bound to the user id, so this must not decrypt.
        _db.StravaAppCredentials.Add(new StravaAppCredential
        {
            UserId                = 2,
            ClientId              = userOne.ClientId,
            ClientSecretEncrypted = userOne.ClientSecretEncrypted,
        });
        await _db.SaveChangesAsync();

        Func<Task> act = () => _sut.ResolveAsync(userId: 2);

        await act.Should().ThrowAsync<StravaCredentialException>()
            .WithMessage("*could not be read*");
    }

    [Fact]
    public async Task ResolveAsync_NoCredentials_ThrowsWithSetupInstructions()
    {
        Func<Task> act = () => _sut.ResolveAsync(userId: 1);

        await act.Should().ThrowAsync<StravaCredentialException>()
            .WithMessage("*strava.com/settings/api*");
    }

    [Fact]
    public async Task GetStatusAsync_NoCredentials_ReportsNotConfigured()
    {
        StravaCredentialStatusDto status = await _sut.GetStatusAsync(userId: 1);

        status.IsConfigured.Should().BeFalse();
        status.ClientId.Should().BeNull();
        status.UpdatedAt.Should().BeNull();
    }

    [Fact]
    public async Task GetStatusAsync_Configured_ReturnsClientIdAndCallbackDomain()
    {
        await _sut.SaveAsync(userId: 1, Input());

        StravaCredentialStatusDto status = await _sut.GetStatusAsync(userId: 1);

        status.IsConfigured.Should().BeTrue();
        status.ClientId.Should().Be(ClientId);
        status.UpdatedAt.Should().NotBeNull();

        // Strava's "Authorization Callback Domain" field wants a bare host, not a full URL.
        status.CallbackDomain.Should().Be("api.runsync.example");
        status.RedirectUri.Should().Be("https://api.runsync.example/api/strava/callback");
    }

    [Fact]
    public async Task GetStatusAsync_NeverExposesTheSecret()
    {
        await _sut.SaveAsync(userId: 1, Input());

        StravaCredentialStatusDto status = await _sut.GetStatusAsync(userId: 1);

        // Guards against someone later adding a secret-bearing property to the status DTO.
        string serialised = System.Text.Json.JsonSerializer.Serialize(status);
        serialised.Should().NotContain(ClientSecret);
    }

    [Fact]
    public async Task DeleteAsync_RemovesCredentialsAndTokens()
    {
        await _sut.SaveAsync(userId: 1, Input());
        await AddTokenAsync(userId: 1);

        await _sut.DeleteAsync(userId: 1);

        (await _db.StravaAppCredentials.AnyAsync(c => c.UserId == 1)).Should().BeFalse();
        // Without the app's secret these tokens can never be refreshed, so they go too.
        (await _db.StravaTokens.AnyAsync(t => t.UserId == 1)).Should().BeFalse();
    }

    [Fact]
    public async Task DeleteAsync_NothingStored_DoesNotThrow()
    {
        Func<Task> act = () => _sut.DeleteAsync(userId: 1);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ExistsAsync_ReflectsWhetherCredentialsAreStored()
    {
        (await _sut.ExistsAsync(userId: 1)).Should().BeFalse();

        await _sut.SaveAsync(userId: 1, Input());

        (await _sut.ExistsAsync(userId: 1)).Should().BeTrue();
    }

    private async Task AddTokenAsync(int userId)
    {
        _db.StravaTokens.Add(new StravaToken
        {
            UserId = userId,
            AccessToken = "access",
            RefreshToken = "refresh",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(6).ToUnixTimeSeconds(),
            LastSyncedAt = DateTime.MinValue,
        });
        await _db.SaveChangesAsync();
    }

    public void Dispose() => _db.Dispose();
}
