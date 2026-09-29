namespace HanoiCheck.Configuration;

public static class DemoLicenseOptions
{
    public static readonly bool Enabled = true;

    public static readonly DateTime ExpirationDate = new(2026, 10, 10);

    public static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(10);
}
