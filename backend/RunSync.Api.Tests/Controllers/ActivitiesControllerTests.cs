// Tests/Controllers/ActivitiesControllerTests.cs
// Unit tests for ActivitiesController: verifies that the controller correctly delegates
// to IActivityService, extracts the userId from the JWT, and returns the expected
// HTTP status codes.
//
// IActivityService and ITokenService are mocked with Moq — the controller has no
// business logic, so these tests focus entirely on HTTP contract correctness.

using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RunSync.Api.Controllers;
using RunSync.Api.Models.DTOs.Activities;
using RunSync.Api.Services.Interfaces;
using Xunit;

namespace RunSync.Api.Tests.Controllers;

public class ActivitiesControllerTests
{
    private readonly Mock<IActivityService> _activityServiceMock = new();
    private readonly Mock<ITokenService> _tokenServiceMock = new();

    private ActivitiesController BuildController(int userId = 1)
    {
        _tokenServiceMock
            .Setup(s => s.GetUserIdFromToken(It.IsAny<System.Security.Claims.ClaimsPrincipal>()))
            .Returns(userId);

        return new ActivitiesController(
            _activityServiceMock.Object,
            _tokenServiceMock.Object,
            NullLogger<ActivitiesController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
    }

    [Fact]
    public async Task GetTrainingPlan_Returns200WithPlanData()
    {
        List<TrainingDayActivityDto> fakePlan =
        [
            new() { PlanDate = DateTime.Today, DayLabel = "Week 1 · Monday", WorkoutType = "Easy Run", PlannedMiles = 3.0f }
        ];

        _activityServiceMock
            .Setup(s => s.GetMatchedActivitiesAsync(1))
            .ReturnsAsync(fakePlan);

        ActivitiesController controller = BuildController(userId: 1);

        IActionResult result = await controller.GetTrainingPlan();

        OkObjectResult ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.StatusCode.Should().Be(200);

        List<TrainingDayActivityDto>? returned = ok.Value as List<TrainingDayActivityDto>;
        returned.Should().HaveCount(1);
        returned![0].WorkoutType.Should().Be("Easy Run");
    }

    [Fact]
    public async Task GetTrainingPlan_CallsServiceWithCorrectUserId()
    {
        _activityServiceMock
            .Setup(s => s.GetMatchedActivitiesAsync(It.IsAny<int>()))
            .ReturnsAsync([]);

        ActivitiesController controller = BuildController(userId: 7);

        await controller.GetTrainingPlan();

        // Verify the service was called with exactly the userId from the token
        _activityServiceMock.Verify(s => s.GetMatchedActivitiesAsync(7), Times.Once);
    }

    [Fact]
    public async Task GetTrainingPlan_ReturnsEmptyListWhenNoPlanDays()
    {
        _activityServiceMock
            .Setup(s => s.GetMatchedActivitiesAsync(It.IsAny<int>()))
            .ReturnsAsync([]);

        ActivitiesController controller = BuildController();

        IActionResult result = await controller.GetTrainingPlan();

        OkObjectResult ok = result.Should().BeOfType<OkObjectResult>().Subject;
        (ok.Value as List<TrainingDayActivityDto>).Should().BeEmpty();
    }

    [Fact]
    public async Task GetSyncStatus_Returns200WithStatusObject()
    {
        _activityServiceMock
            .Setup(s => s.GetSyncStatusAsync(1))
            .ReturnsAsync((new DateTime(2026, 3, 1), 42, true));

        ActivitiesController controller = BuildController(userId: 1);

        IActionResult result = await controller.GetSyncStatus();

        OkObjectResult ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.StatusCode.Should().Be(200);
        ok.Value.Should().NotBeNull();
    }

    [Fact]
    public async Task GetSyncStatus_CallsServiceWithCorrectUserId()
    {
        _activityServiceMock
            .Setup(s => s.GetSyncStatusAsync(It.IsAny<int>()))
            .ReturnsAsync((null, 0, false));

        ActivitiesController controller = BuildController(userId: 13);

        await controller.GetSyncStatus();

        _activityServiceMock.Verify(s => s.GetSyncStatusAsync(13), Times.Once);
    }

    [Fact]
    public async Task GetSyncStatus_WhenNotConnected_ReturnsIsConnectedFalse()
    {
        _activityServiceMock
            .Setup(s => s.GetSyncStatusAsync(It.IsAny<int>()))
            .ReturnsAsync((null, 0, false));

        ActivitiesController controller = BuildController();

        IActionResult result = await controller.GetSyncStatus();

        OkObjectResult ok = result.Should().BeOfType<OkObjectResult>().Subject;

        // Use anonymous type reflection to verify the isConnected field
        dynamic? value = ok.Value;
        ((bool)value!.GetType().GetProperty("isConnected")!.GetValue(value)!).Should().BeFalse();
    }
}
