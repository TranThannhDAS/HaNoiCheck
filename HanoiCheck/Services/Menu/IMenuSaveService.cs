using HanoiCheck.Models;

namespace HanoiCheck.Services.Menu;

public interface IMenuSaveService
{
    Task<MenuSaveSummary> SaveSequentiallyAsync(
        IReadOnlyCollection<ResolvedMenuRow> rows,
        IProgress<MenuSaveProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
