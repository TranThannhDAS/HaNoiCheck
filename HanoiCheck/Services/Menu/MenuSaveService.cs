using HanoiCheck.Models;
using HanoiCheck.Services.Licensing;

namespace HanoiCheck.Services.Menu;

public sealed class MenuSaveService(
    IMenuApiService menuApi,
    IDemoLicenseService demoLicense)
    : IMenuSaveService
{
    public async Task<MenuSaveSummary> SaveSequentiallyAsync(
        IReadOnlyCollection<ResolvedMenuRow> rows,
        IProgress<MenuSaveProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var pending = rows.Where(row =>
                row.ValidationStatus == ImportRowStatus.Valid &&
                row.SaveStatus != BatchSaveStatus.Success &&
                row.LastSaveOutcome is not SaveBatchOutcome.Unauthorized and
                    not SaveBatchOutcome.Forbidden and
                    not SaveBatchOutcome.LicenseBlocked and
                    not SaveBatchOutcome.Unknown and
                    not SaveBatchOutcome.Cancelled)
            .ToList();
        var summary = new MenuSaveSummary { Total = pending.Count };

        var license = await demoLicense.CheckAsync(false, cancellationToken);
        if (!license.CanUseApplication)
        {
            summary.StopReason = SaveBatchOutcome.LicenseBlocked;
            summary.Message = license.ErrorMessage ?? "Phiên bản dùng thử đã hết hạn.";
            return summary;
        }

        for (var index = 0; index < pending.Count; index++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                summary.WasCancelled = true;
                summary.Message = $"Đã dừng. {summary.Processed}/{summary.Total} thực đơn đã được xử lý.";
                break;
            }

            var row = pending[index];
            row.SaveStatus = BatchSaveStatus.Saving;
            row.SaveMessage = null;
            Report(progress, index + 1, summary, row);

            var result = await menuApi.SaveMenuAsync(row, cancellationToken);
            summary.Processed++;
            row.LastSaveOutcome = result.Outcome;
            row.SaveMessage = result.Message;
            switch (result.Outcome)
            {
                case SaveBatchOutcome.Success:
                    row.SaveStatus = BatchSaveStatus.Success;
                    row.SavedAt = DateTimeOffset.Now;
                    row.MenuId = result.MenuId;
                    summary.SuccessCount++;
                    break;
                case SaveBatchOutcome.Unknown:
                case SaveBatchOutcome.Cancelled:
                    row.SaveStatus = BatchSaveStatus.Unknown;
                    summary.UnknownCount++;
                    if (result.Outcome == SaveBatchOutcome.Cancelled)
                    {
                        summary.WasCancelled = true;
                    }
                    break;
                default:
                    row.SaveStatus = BatchSaveStatus.Failed;
                    summary.FailedCount++;
                    break;
            }

            Report(progress, index + 1, summary, row);
            if (result.Outcome is SaveBatchOutcome.Unauthorized or SaveBatchOutcome.Forbidden or
                SaveBatchOutcome.LicenseBlocked or SaveBatchOutcome.Cancelled)
            {
                summary.StopReason = result.Outcome;
                summary.Message = result.Message;
                break;
            }
        }

        summary.Message ??= summary.WasCancelled
            ? $"Đã dừng. {summary.Processed}/{summary.Total} thực đơn đã được xử lý."
            : "Hoàn tất lưu thực đơn.";
        return summary;
    }

    private static void Report(
        IProgress<MenuSaveProgress>? progress,
        int current,
        MenuSaveSummary summary,
        ResolvedMenuRow row) => progress?.Report(new MenuSaveProgress
    {
        Current = current,
        Total = summary.Total,
        CurrentRow = row,
        SuccessCount = summary.SuccessCount,
        FailedCount = summary.FailedCount,
        UnknownCount = summary.UnknownCount
    });
}
