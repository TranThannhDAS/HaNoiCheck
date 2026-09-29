using HanoiCheck.Models;
using HanoiCheck.Services.Licensing;

namespace HanoiCheck.Services;

public sealed class BatchImportSaveService(
    IBatchApiService batchApi,
    IDemoLicenseService demoLicense)
    : IBatchImportSaveService
{
    public async Task<BatchSaveSummary> SaveSequentiallyAsync(
        IReadOnlyCollection<ResolvedBatchRow> rows,
        IProgress<BatchSaveProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var pendingRows = rows
            .Where(row => row.Status == ImportRowStatus.Valid &&
                          row.SaveStatus != BatchSaveStatus.Success &&
                          row.LastSaveOutcome is not SaveBatchOutcome.Unauthorized and
                              not SaveBatchOutcome.Forbidden and
                              not SaveBatchOutcome.LicenseBlocked and
                              not SaveBatchOutcome.Unknown and
                              not SaveBatchOutcome.Cancelled)
            .ToList();
        var summary = new BatchSaveSummary { Total = pendingRows.Count };

        var licenseStatus = await demoLicense.CheckAsync(false, cancellationToken);
        if (!licenseStatus.CanUseApplication)
        {
            summary.StopReason = SaveBatchOutcome.LicenseBlocked;
            summary.Message = licenseStatus.ErrorMessage ?? "Phiên bản dùng thử đã hết hạn.";
            return summary;
        }

        for (var index = 0; index < pendingRows.Count; index++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                summary.WasCancelled = true;
                summary.Message = $"Đã dừng. {summary.Processed}/{summary.Total} dòng đã được xử lý.";
                break;
            }

            var row = pendingRows[index];
            row.SaveStatus = BatchSaveStatus.Saving;
            row.SaveMessage = null;
            Report(progress, index + 1, summary, row);

            var result = await batchApi.SaveBatchAsync(row, cancellationToken);
            summary.Processed++;
            row.LastSaveOutcome = result.Outcome;
            row.SaveMessage = result.Message;

            switch (result.Outcome)
            {
                case SaveBatchOutcome.Success:
                    row.SaveStatus = BatchSaveStatus.Success;
                    row.SavedAt = DateTimeOffset.Now;
                    row.BatchId = result.BatchId;
                    summary.SuccessCount++;
                    break;
                case SaveBatchOutcome.Unknown:
                case SaveBatchOutcome.Cancelled:
                    row.SaveStatus = BatchSaveStatus.Unknown;
                    summary.UnknownCount++;
                    if (result.Outcome == SaveBatchOutcome.Cancelled)
                    {
                        summary.WasCancelled = true;
                        summary.StopReason = result.Outcome;
                    }
                    break;
                default:
                    row.SaveStatus = BatchSaveStatus.Failed;
                    summary.FailedCount++;
                    break;
            }

            Report(progress, index + 1, summary, row);

            if (result.Outcome is SaveBatchOutcome.Unauthorized or
                SaveBatchOutcome.Forbidden or SaveBatchOutcome.LicenseBlocked or
                SaveBatchOutcome.Cancelled)
            {
                summary.StopReason = result.Outcome;
                summary.Message = result.Message;
                break;
            }
        }

        summary.Message ??= summary.WasCancelled
            ? $"Đã dừng. {summary.Processed}/{summary.Total} dòng đã được xử lý."
            : "Hoàn tất lưu dữ liệu.";
        return summary;
    }

    private static void Report(
        IProgress<BatchSaveProgress>? progress,
        int current,
        BatchSaveSummary summary,
        ResolvedBatchRow row) => progress?.Report(new BatchSaveProgress
    {
        Current = current,
        Total = summary.Total,
        CurrentRow = row,
        SuccessCount = summary.SuccessCount,
        FailedCount = summary.FailedCount,
        UnknownCount = summary.UnknownCount
    });
}
