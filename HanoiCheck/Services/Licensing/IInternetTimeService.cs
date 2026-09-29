namespace HanoiCheck.Services.Licensing;

public interface IInternetTimeService
{
    Task<DateTimeOffset?> GetCurrentTimeAsync(
        CancellationToken cancellationToken = default);
}
