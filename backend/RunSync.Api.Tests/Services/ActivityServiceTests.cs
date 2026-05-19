// Tests/Services/ActivityServiceTests.cs
// Unit tests for ActivityService: verifies date-matching logic, unit conversions,
// and the shape of the returned TrainingDayActivityDto list.
//
// Uses an in-memory SQLite database via EF Core to avoid mocking DbContext directly
// (testing against a real DB surface area catches more bugs than a mock would).

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using RunSync.Api.Data;
using RunSync.Api.Models.DTOs.Activities;
using RunSync.Api.Models.Entities;
using RunSync.Api.Services;
using Xunit;

namespace RunSync.Api.Tests.Services;

public class ActivityServiceTests : IDisposable
{
    private readonly RunSyncDbContext _db;
    private readonly ActivityService _sut;

    public ActivityServiceTests()
    {
        DbContextOptions<RunSyncDbContext> options = new DbContextOptionsBuilder<RunSyncDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _db = new RunSyncDbContext(options);

        // Seed a user
        _db.Users.Add(new User { Id = 1, Email = "runner@test.com", DisplayName = "Runner", PasswordHash = "x" });
        _db.SaveChanges();

        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                // Use a deterministic start date for predictable plan day assertions
                ["TrainingPlan:StartDate"] = "2026-02-02"
            })
            .Build();

        _sut = new ActivityService(_db, config, NullLogger<ActivityService>.Instance);
    }

    [Fact]
    public async Task GetMatchedActivitiesAsync_Returns84Days()
    {
        List<TrainingDayActivityDto> result = await _sut.GetMatchedActivitiesAsync(userId: 1);

        // 12 weeks × 7 days = 84 plan days
        result.Should().HaveCount(84);
    }

    [Fact]
    public async Task GetMatchedActivitiesAsync_FirstDayIsWeek1Monday()
    {
        List<TrainingDayActivityDto> result = await _sut.GetMatchedActivitiesAsync(userId: 1);

        result[0].PlanDate.Should().Be(new DateTime(2026, 2, 2));
        result[0].DayLabel.Should().Be("Week 1 · Monday");
    }

    [Fact]
    public async Task GetMatchedActivitiesAsync_LastDayIsRaceDay()
    {
        List<TrainingDayActivityDto> result = await _sut.GetMatchedActivitiesAsync(userId: 1);

        TrainingDayActivityDto lastDay = result[^1];
        lastDay.WorkoutType.Should().Be("Race Day");
        lastDay.PlannedMiles.Should().Be(13.1f);
    }

    [Fact]
    public async Task GetMatchedActivitiesAsync_MatchesActivityToCorrectPlanDay()
    {
        // Place an activity on Week 1 Monday (the first plan day: 2026-02-02)
        _db.StravaActivities.Add(new StravaActivity
        {
            Id = 1001,
            UserId = 1,
            Name = "Monday Morning Run",
            Type = "Run",
            DistanceMeters = 3218.68f,   // ~2.0 miles
            MovingTimeSeconds = 1200,    // 20 minutes
            StartDateLocal = new DateTime(2026, 2, 2, 7, 0, 0),
            StartDateUtc = new DateTime(2026, 2, 2, 12, 0, 0)
        });
        await _db.SaveChangesAsync();

        List<TrainingDayActivityDto> result = await _sut.GetMatchedActivitiesAsync(userId: 1);

        TrainingDayActivityDto monday = result[0];
        monday.IsCompleted.Should().BeTrue();
        monday.StravaActivityId.Should().Be(1001);
        monday.ActivityName.Should().Be("Monday Morning Run");
    }

    [Fact]
    public async Task GetMatchedActivitiesAsync_UnmatchedDayHasNullActivityFields()
    {
        // No activities seeded — all plan days should be unmatched
        List<TrainingDayActivityDto> result = await _sut.GetMatchedActivitiesAsync(userId: 1);

        TrainingDayActivityDto anyDay = result[2]; // Wednesday Week 1
        anyDay.IsCompleted.Should().BeFalse();
        anyDay.StravaActivityId.Should().BeNull();
        anyDay.ActualMiles.Should().BeNull();
        anyDay.ActualPace.Should().BeNull();
    }

    [Fact]
    public async Task GetMatchedActivitiesAsync_ConvertsMetersToMilesCorrectly()
    {
        _db.StravaActivities.Add(new StravaActivity
        {
            Id = 2001,
            UserId = 1,
            Name = "Test Run",
            Type = "Run",
            DistanceMeters = 8046.7f,    // Exactly 5.0 miles
            MovingTimeSeconds = 2500,
            StartDateLocal = new DateTime(2026, 2, 2, 7, 0, 0),
            StartDateUtc = new DateTime(2026, 2, 2, 12, 0, 0)
        });
        await _db.SaveChangesAsync();

        List<TrainingDayActivityDto> result = await _sut.GetMatchedActivitiesAsync(userId: 1);

        result[0].ActualMiles.Should().BeApproximately(5.0f, precision: 0.05f);
    }

    [Fact]
    public async Task GetMatchedActivitiesAsync_FormatsPaceCorrectly()
    {
        // 1 mile in 8 minutes = 480 seconds → pace "8:00 /mi"
        _db.StravaActivities.Add(new StravaActivity
        {
            Id = 3001,
            UserId = 1,
            Name = "Tempo Run",
            Type = "Run",
            DistanceMeters = 1609.34f,   // 1 mile
            MovingTimeSeconds = 480,     // 8 minutes
            StartDateLocal = new DateTime(2026, 2, 2, 7, 0, 0),
            StartDateUtc = new DateTime(2026, 2, 2, 12, 0, 0)
        });
        await _db.SaveChangesAsync();

        List<TrainingDayActivityDto> result = await _sut.GetMatchedActivitiesAsync(userId: 1);

        result[0].ActualPace.Should().Be("8:00 /mi");
    }

    [Fact]
    public async Task GetSyncStatusAsync_ReturnsNotConnectedWhenNoToken()
    {
        (DateTime? lastSyncedAt, int totalActivities, bool isConnected) =
            await _sut.GetSyncStatusAsync(userId: 1);

        isConnected.Should().BeFalse();
        totalActivities.Should().Be(0);
        lastSyncedAt.Should().BeNull();
    }

    [Fact]
    public async Task GetSyncStatusAsync_ReturnsConnectedWhenTokenExists()
    {
        _db.StravaTokens.Add(new StravaToken
        {
            UserId = 1,
            AccessToken = "access",
            RefreshToken = "refresh",
            ExpiresAt = 9999999999,
            LastSyncedAt = new DateTime(2026, 3, 1, 10, 0, 0)
        });
        await _db.SaveChangesAsync();

        (DateTime? lastSyncedAt, int totalActivities, bool isConnected) =
            await _sut.GetSyncStatusAsync(userId: 1);

        isConnected.Should().BeTrue();
        lastSyncedAt.Should().Be(new DateTime(2026, 3, 1, 10, 0, 0));
    }

    public void Dispose() => _db.Dispose();
}
