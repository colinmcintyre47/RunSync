using RunSync.Api.Models.DTOs.Training;

namespace RunSync.Api.Services.Interfaces;

public interface ITrainingPlanService
{
    Task<TrainingGoalDto?> GetGoalAsync(int userId);
    Task<TrainingGoalDto> SaveGoalAsync(int userId, TrainingGoalInputDto dto);
    Task DeleteGoalAsync(int userId);
    Task<TrainingPlanDto?> GetTrainingPlanAsync(int userId);
}
