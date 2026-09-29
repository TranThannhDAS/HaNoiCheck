using HanoiCheck.Models;

namespace HanoiCheck.Services.Licensing;

public interface IDemoLicenseService
{
    event Action? StatusChanged;

    DemoLicenseStatus? CurrentStatus { get; }

    Task<DemoLicenseStatus> CheckAsync(
        bool forceRefresh = false,
        CancellationToken cancellationToken = default);
}
