using HanoiCheck.Models;

namespace HanoiCheck.Services;

public interface IBatchImportSaveService
{
    Task<BatchSaveSummary> SaveSequentiallyAsync(
        IReadOnlyCollection<ResolvedBatchRow> rows,
        IProgress<BatchSaveProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
