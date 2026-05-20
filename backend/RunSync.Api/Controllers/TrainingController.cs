using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RunSync.Api.Models.DTOs.Training;
using RunSync.Api.Services.Interfaces;
using System.Security.Claims;

namespace RunSync.Api.Controllers;

[ApiController]
[Route("api/training")]
[Authorize]
public class TrainingController : ControllerBase
{
    private readonly ITrainingPlanService _trainingPlanService;

    public TrainingController(ITrainingPlanService trainingPlanService)
        => _trainingPlanService = trainingPlanService;

    private int UserId =>
        int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet("goal")]
    public async Task<IActionResult> GetGoal()
    {
        var goal = await _trainingPlanService.GetGoalAsync(UserId);
        if (goal is null) return NoContent();
        return Ok(goal);
    }

    [HttpPost("goal")]
    public async Task<IActionResult> SaveGoal([FromBody] TrainingGoalInputDto dto)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        var goal = await _trainingPlanService.SaveGoalAsync(UserId, dto);
        return Ok(goal);
    }

    [HttpDelete("goal")]
    public async Task<IActionResult> DeleteGoal()
    {
        await _trainingPlanService.DeleteGoalAsync(UserId);
        return NoContent();
    }

    [HttpGet("plan")]
    public async Task<IActionResult> GetPlan()
    {
        var plan = await _trainingPlanService.GetTrainingPlanAsync(UserId);
        if (plan is null) return NoContent();
        return Ok(plan);
    }
}
