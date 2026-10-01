using HanoiCheck.Models;

namespace HanoiCheck.Services.Menu;

public interface IMenuApiService
{
    Task<ServiceResult<IReadOnlyList<StudentGroupOption>>> GetStudentGroupsAsync(
        bool forceRefresh = false,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<IReadOnlyList<SchoolOption>>> GetSchoolOptionsAsync(
        bool forceRefresh = false,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<IReadOnlyList<DishOption>>> GetDishOptionsAsync(
        int studentGroupId,
        CancellationToken cancellationToken = default);

    Task<MenuSaveResult> SaveMenuAsync(
        ResolvedMenuRow row,
        CancellationToken cancellationToken = default);

    void ClearCache();
}
