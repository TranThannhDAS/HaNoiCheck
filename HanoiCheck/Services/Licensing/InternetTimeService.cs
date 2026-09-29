using System.Net.Http;
using Microsoft.Extensions.Logging;

namespace HanoiCheck.Services.Licensing;

public sealed class InternetTimeService(
    HttpClient httpClient,
    ILogger<InternetTimeService> logger) : IInternetTimeService
{
    public async Task<DateTimeOffset?> GetCurrentTimeAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var headRequest = new HttpRequestMessage(HttpMethod.Head, string.Empty);
            using var headResponse = await httpClient.SendAsync(
                headRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (headResponse.Headers.Date is DateTimeOffset headDate)
            {
                return headDate;
            }

            using var getRequest = new HttpRequestMessage(HttpMethod.Get, string.Empty);
            using var getResponse = await httpClient.SendAsync(
                getRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            return getResponse.Headers.Date;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Internet time request timed out");
            return null;
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Could not retrieve Internet time");
            return null;
        }
    }
}
