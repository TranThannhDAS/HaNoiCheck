namespace HanoiCheck.Configuration;

public static class DemoLicenseOptions
{
    public static readonly bool Enabled = false;

    public static readonly DateTime ExpirationDate = new(2026, 10, 20);

    public static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(10);
}
