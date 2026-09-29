using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using HanoiCheck.Models;

namespace HanoiCheck.Services;

public sealed class AuthService(HttpClient httpClient, AuthStateService authState)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<ServiceResult<CaptchaData>> GetCaptchaAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await httpClient.GetAsync(
                "supplier/captcha", cancellationToken);
            var payload = await ReadResponseAsync<CaptchaResponse>(response, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return ServiceResult<CaptchaData>.Failure(
                    payload?.Message ?? "Không thể tải mã xác thực.");
            }

            if (payload is not { Success: true, Data: not null } ||
                string.IsNullOrWhiteSpace(payload.Data.Key) ||
                string.IsNullOrWhiteSpace(payload.Data.Image))
            {
                return ServiceResult<CaptchaData>.Failure(
                    payload?.Message ?? "Dữ liệu mã xác thực không hợp lệ.");
            }

            return ServiceResult<CaptchaData>.Success(payload.Data);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ServiceResult<CaptchaData>.Failure(
                "Yêu cầu đã hết thời gian chờ. Vui lòng thử lại.");
        }
        catch (HttpRequestException)
        {
            return ServiceResult<CaptchaData>.Failure(
                "Không thể kết nối đến máy chủ. Vui lòng kiểm tra mạng.");
        }
        catch (JsonException)
        {
            return ServiceResult<CaptchaData>.Failure(
                "Máy chủ trả về dữ liệu không hợp lệ.");
        }
        catch (Exception)
        {
            return ServiceResult<CaptchaData>.Failure(
                "Không thể tải mã xác thực. Vui lòng thử lại.");
        }
    }

    public async Task<ServiceResult<LoginData>> LoginAsync(
        LoginRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await httpClient.PostAsJsonAsync(
                "supplier/login", request, JsonOptions, cancellationToken);
            var payload = await ReadResponseAsync<LoginResponse>(response, cancellationToken);

            if (!response.IsSuccessStatusCode || payload is not { Success: true, Data: not null })
            {
                return ServiceResult<LoginData>.Failure(
                    payload?.Message ?? "Đăng nhập không thành công. Vui lòng kiểm tra lại thông tin.");
            }

            if (string.IsNullOrWhiteSpace(payload.Data.AccessToken))
            {
                return ServiceResult<LoginData>.Failure(
                    "Phản hồi đăng nhập không chứa access token.");
            }

            authState.SetAuthenticated(payload.Data);
            return ServiceResult<LoginData>.Success(payload.Data);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ServiceResult<LoginData>.Failure(
                "Yêu cầu đã hết thời gian chờ. Vui lòng thử lại.");
        }
        catch (HttpRequestException)
        {
            return ServiceResult<LoginData>.Failure(
                "Không thể kết nối đến máy chủ. Vui lòng kiểm tra mạng.");
        }
        catch (JsonException)
        {
            return ServiceResult<LoginData>.Failure(
                "Máy chủ trả về dữ liệu không hợp lệ.");
        }
        catch (Exception)
        {
            return ServiceResult<LoginData>.Failure(
                "Đã xảy ra lỗi khi đăng nhập. Vui lòng thử lại.");
        }
    }

    private static async Task<T?> ReadResponseAsync<T>(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken);
    }
}
