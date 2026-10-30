namespace HanoiCheck.Configuration;

public sealed class AccountAccessOptions
{
    public const string SectionName = "AccountAccess";

    public List<string> AllowedAccounts { get; set; } = [];

    public string ContactZalo { get; set; } = string.Empty;

    public bool IsAllowed(string? username) =>
        !string.IsNullOrWhiteSpace(username) &&
        AllowedAccounts.Any(account =>
            string.Equals(account?.Trim(), username.Trim(), StringComparison.OrdinalIgnoreCase));
}
