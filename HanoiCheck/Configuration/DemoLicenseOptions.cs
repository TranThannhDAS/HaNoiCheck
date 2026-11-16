namespace HanoiCheck.Configuration;

public static class DemoLicenseOptions
{
    public static readonly bool Enabled = false;

    public static readonly DateTime ExpirationDate = new(2026, 11, 30);

    public static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(10);
}
