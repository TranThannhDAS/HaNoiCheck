using HanoiCheck.Models;

namespace HanoiCheck.Services;

public interface IBatchApiService
{
    Task<ServiceResult<FormOptionsData>> GetFormOptionsAsync(
        bool forceRefresh = false,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<FoodOptionsData>> GetFoodOptionsAsync(
        int supplierFoodId,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<ProcessDetailData>> GetProcessAsync(
        int processId,
        CancellationToken cancellationToken = default);

    Task<SaveBatchResult> SaveBatchAsync(
        ResolvedBatchRow row,
        CancellationToken cancellationToken = default);

    void ClearCache();
}
