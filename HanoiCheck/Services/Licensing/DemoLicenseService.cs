using System.Diagnostics;
using HanoiCheck.Configuration;
using HanoiCheck.Models;
using Microsoft.Extensions.Logging;

namespace HanoiCheck.Services.Licensing;

public sealed class DemoLicenseService(
    IInternetTimeService internetTimeService,
    ILogger<DemoLicenseService> logger) : IDemoLicenseService
{
    private static readonly TimeZoneInfo VietnamTimeZone = GetVietnamTimeZone();
    private readonly SemaphoreSlim _checkLock = new(1, 1);
    private long? _lastVerifiedTimestamp;

    public event Action? StatusChanged;

    public DemoLicenseStatus? CurrentStatus { get; private set; }

    public async Task<DemoLicenseStatus> CheckAsync(
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        if (!DemoLicenseOptions.Enabled)
        {
            return SetStatus(new DemoLicenseStatus
            {
                State = DemoLicenseState.Disabled,
                IsEnabled = false,
                ExpirationDate = DemoLicenseOptions.ExpirationDate,
                CouldVerifyOnline = true
            });
        }

        if (!forceRefresh && TryGetCachedStatus(out var cached))
        {
            return cached;
        }

        await _checkLock.WaitAsync(cancellationToken);
        try
        {
            if (!forceRefresh && TryGetCachedStatus(out cached))
            {
                return cached;
            }

            var internetTime = await internetTimeService.GetCurrentTimeAsync(cancellationToken);
            if (internetTime is null)
            {
                _lastVerifiedTimestamp = null;
                var cannotVerify = new DemoLicenseStatus
                {
                    State = DemoLicenseState.CannotVerify,
                    IsEnabled = true,
                    ExpirationDate = DemoLicenseOptions.ExpirationDate,
                    CouldVerifyOnline = false,
                    ErrorMessage = "Không thể xác minh thời hạn sử dụng do không kết nối được Internet. Vui lòng kiểm tra kết nối mạng."
                };
                logger.LogWarning("Demo license status: CannotVerify");
                return SetStatus(cannotVerify);
            }

            var vietnamTime = TimeZoneInfo.ConvertTime(internetTime.Value, VietnamTimeZone);
            var expirationBoundary = new DateTimeOffset(
                DemoLicenseOptions.ExpirationDate.Date.AddDays(1), TimeSpan.FromHours(7));
            var isExpired = vietnamTime >= expirationBoundary;
            var status = new DemoLicenseStatus
            {
                State = isExpired ? DemoLicenseState.Expired : DemoLicenseState.Valid,
                IsEnabled = true,
                CurrentTime = vietnamTime,
                ExpirationDate = DemoLicenseOptions.ExpirationDate,
                CouldVerifyOnline = true
            };
            _lastVerifiedTimestamp = Stopwatch.GetTimestamp();
            logger.LogInformation(
                "Demo license status: {State}; Internet time: {CurrentTime}; Expiration date: {ExpirationDate}",
                status.State, status.CurrentTime, status.ExpirationDate);
            return SetStatus(status);
        }
        finally
        {
            _checkLock.Release();
        }
    }

    private bool TryGetCachedStatus(out DemoLicenseStatus status)
    {
        status = CurrentStatus!;
        return CurrentStatus is { CouldVerifyOnline: true } &&
               _lastVerifiedTimestamp is long timestamp &&
               Stopwatch.GetElapsedTime(timestamp) <= DemoLicenseOptions.CacheDuration;
    }

    private DemoLicenseStatus SetStatus(DemoLicenseStatus status)
    {
        var changed = CurrentStatus?.State != status.State ||
                      CurrentStatus?.CurrentTime != status.CurrentTime;
        CurrentStatus = status;
        if (changed)
        {
            StatusChanged?.Invoke();
        }

        return status;
    }

    private static TimeZoneInfo GetVietnamTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.CreateCustomTimeZone(
                "Vietnam Standard Time", TimeSpan.FromHours(7),
                "Vietnam Standard Time", "Vietnam Standard Time");
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.CreateCustomTimeZone(
                "Vietnam Standard Time", TimeSpan.FromHours(7),
                "Vietnam Standard Time", "Vietnam Standard Time");
        }
    }
}
