namespace HanoiCheck.Models;

public enum DemoLicenseState
{
    Disabled,
    Valid,
    Expired,
    CannotVerify
}

public sealed class DemoLicenseStatus
{
    public DemoLicenseState State { get; init; }
    public bool IsEnabled { get; init; }
    public bool IsExpired => State == DemoLicenseState.Expired;
    public DateTimeOffset? CurrentTime { get; init; }
    public DateTime ExpirationDate { get; init; }
    public string? ErrorMessage { get; init; }
    public bool CouldVerifyOnline { get; init; }
    public bool CanUseApplication => State is DemoLicenseState.Disabled or DemoLicenseState.Valid;
}
