using HanoiCheck.Models;

namespace HanoiCheck.Services;

public sealed class AuthStateService
{
    public event Action? StateChanged;

    public string? AccessToken { get; private set; }
    public string? RefreshToken { get; private set; }
    public int ExpiresIn { get; private set; }
    public int RefreshExpiresIn { get; private set; }
    public bool IsAuthenticated => !string.IsNullOrWhiteSpace(AccessToken);

    public void SetAuthenticated(LoginData loginData)
    {
        AccessToken = loginData.AccessToken;
        RefreshToken = loginData.RefreshToken;
        ExpiresIn = loginData.ExpiresIn;
        RefreshExpiresIn = loginData.RefreshExpiresIn;
        StateChanged?.Invoke();
    }

    public void Clear()
    {
        AccessToken = null;
        RefreshToken = null;
        ExpiresIn = 0;
        RefreshExpiresIn = 0;
        StateChanged?.Invoke();
    }
}
