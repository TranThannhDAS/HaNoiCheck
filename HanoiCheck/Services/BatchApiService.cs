using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using HanoiCheck.Models;
using HanoiCheck.Services.Licensing;
using Microsoft.Extensions.Logging;

namespace HanoiCheck.Services;

public sealed class BatchApiService(
    HttpClient httpClient,
    AuthStateService authState,
    BatchPayloadBuilder payloadBuilder,
    IDemoLicenseService demoLicense,
    ILogger<BatchApiService> logger)
    : IBatchApiService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly Dictionary<int, FoodOptionsData> _foodOptionsCache = [];
    private readonly Dictionary<int, ProcessDetailData> _processCache = [];
    private FormOptionsData? _formOptionsCache;

    public async Task<ServiceResult<FormOptionsData>> GetFormOptionsAsync(
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        if (!forceRefresh && _formOptionsCache is not null)
        {
            return ServiceResult<FormOptionsData>.Success(_formOptionsCache);
        }

        var result = await GetAsync<FormOptionsResponse, FormOptionsData>(
            "supplier/batches/form-options", cancellationToken);
        if (result.IsSuccess)
        {
            _formOptionsCache = result.Value;
        }

        return result;
    }

    public async Task<ServiceResult<FoodOptionsData>> GetFoodOptionsAsync(
        int supplierFoodId,
        CancellationToken cancellationToken = default)
    {
        if (_foodOptionsCache.TryGetValue(supplierFoodId, out var cached))
        {
            return ServiceResult<FoodOptionsData>.Success(cached);
        }

        var path = $"supplier/batches/food-options?supplier_food_id={supplierFoodId}";
        var result = await GetAsync<FoodOptionsResponse, FoodOptionsData>(path, cancellationToken);
        if (result.IsSuccess && result.Value is not null)
        {
            _foodOptionsCache[supplierFoodId] = result.Value;
        }

        return result;
    }

    public async Task<ServiceResult<ProcessDetailData>> GetProcessAsync(
        int processId,
        CancellationToken cancellationToken = default)
    {
        if (_processCache.TryGetValue(processId, out var cached))
        {
            return ServiceResult<ProcessDetailData>.Success(cached);
        }

        var result = await GetAsync<ProcessDetailResponse, ProcessDetailData>(
            $"supplier/processes/{processId}", cancellationToken);
        if (result.IsSuccess && result.Value is not null)
        {
            _processCache[processId] = result.Value;
        }

        return result;
    }

    public async Task<SaveBatchResult> SaveBatchAsync(
        ResolvedBatchRow row,
        CancellationToken cancellationToken = default)
    {
        if (row.Status != ImportRowStatus.Valid)
        {
            return new SaveBatchResult(
                SaveBatchOutcome.Failed, "Chỉ có thể lưu dòng đã được resolve hợp lệ.");
        }

        if (row.SaveStatus == BatchSaveStatus.Success)
        {
            return new SaveBatchResult(
                SaveBatchOutcome.Failed, "Dòng này đã được lưu trước đó.");
        }

        var licenseStatus = await demoLicense.CheckAsync(false, cancellationToken);
        if (!licenseStatus.CanUseApplication)
        {
            return new SaveBatchResult(
                SaveBatchOutcome.LicenseBlocked,
                licenseStatus.ErrorMessage ?? "Phiên bản dùng thử đã hết hạn.");
        }

        if (string.IsNullOrWhiteSpace(authState.AccessToken))
        {
            return new SaveBatchResult(
                SaveBatchOutcome.Unauthorized, "Phiên đăng nhập không hợp lệ.", 401);
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var content = payloadBuilder.BuildMultipartContent(row);
            using var request = new HttpRequestMessage(HttpMethod.Post, "supplier/batches/store")
            {
                Content = content
            };
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Bearer", authState.AccessToken);

            using var response = await httpClient.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var payload = TryDeserializeSaveResponse(body);
            var message = GetSaveMessage(payload, body, response.StatusCode);

            logger.LogInformation(
                "Save batch row {RowNumber}, code {BatchCode}: HTTP {StatusCode}, duration {DurationMs} ms",
                row.RowNumber, row.Code, (int)response.StatusCode, stopwatch.ElapsedMilliseconds);

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                ClearCache();
                authState.Clear();
                return new SaveBatchResult(
                    SaveBatchOutcome.Unauthorized,
                    "Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại.",
                    (int)response.StatusCode);
            }

            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                return new SaveBatchResult(
                    SaveBatchOutcome.Forbidden,
                    payload?.Message ?? "Bạn không có quyền lưu lô nhập hàng.",
                    (int)response.StatusCode);
            }

            if (!response.IsSuccessStatusCode || payload is { Success: false })
            {
                return new SaveBatchResult(
                    SaveBatchOutcome.Failed, message, (int)response.StatusCode);
            }

            return new SaveBatchResult(
                SaveBatchOutcome.Success,
                payload?.Message ?? "Lưu lô nhập hàng thành công.",
                (int)response.StatusCode,
                TryGetBatchId(payload?.Data));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(
                "Save batch row {RowNumber}, code {BatchCode} was cancelled after {DurationMs} ms",
                row.RowNumber, row.Code, stopwatch.ElapsedMilliseconds);
            return new SaveBatchResult(
                SaveBatchOutcome.Cancelled,
                "Yêu cầu đã bị hủy; chưa thể xác định lô hiện tại đã được tạo hay chưa.");
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning(
                "Save batch row {RowNumber}, code {BatchCode} timed out after {DurationMs} ms",
                row.RowNumber, row.Code, stopwatch.ElapsedMilliseconds);
            return new SaveBatchResult(
                SaveBatchOutcome.Unknown,
                "Yêu cầu hết thời gian chờ; chưa thể xác định lô đã được tạo hay chưa.");
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex,
                "Save batch row {RowNumber}, code {BatchCode} had a network error after {DurationMs} ms",
                row.RowNumber, row.Code, stopwatch.ElapsedMilliseconds);
            return new SaveBatchResult(
                SaveBatchOutcome.Unknown,
                "Mất kết nối khi lưu; chưa thể xác định lô đã được tạo hay chưa.");
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex,
                "Could not build payload for row {RowNumber}, code {BatchCode}",
                row.RowNumber, row.Code);
            return new SaveBatchResult(
                SaveBatchOutcome.Failed,
                $"Không thể chuẩn bị dữ liệu gửi: {ex.Message}");
        }
    }

    public void ClearCache()
    {
        _formOptionsCache = null;
        _foodOptionsCache.Clear();
        _processCache.Clear();
    }

    private async Task<ServiceResult<TData>> GetAsync<TResponse, TData>(
        string path,
        CancellationToken cancellationToken)
        where TResponse : ApiResponse<TData>
    {
        if (string.IsNullOrWhiteSpace(authState.AccessToken))
        {
            return ServiceResult<TData>.Failure("Phiên đăng nhập không hợp lệ.");
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Bearer", authState.AccessToken);
            using var response = await httpClient.SendAsync(request, cancellationToken);

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                ClearCache();
                authState.Clear();
                return ServiceResult<TData>.Failure(
                    "Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại.");
            }

            TResponse? payload = null;
            if (response.Content.Headers.ContentLength is not 0)
            {
                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                payload = await JsonSerializer.DeserializeAsync<TResponse>(
                    stream, JsonOptions, cancellationToken);
            }

            if (!response.IsSuccessStatusCode || payload is not { Success: true, Data: not null })
            {
                return ServiceResult<TData>.Failure(
                    payload?.Message ?? $"API trả về lỗi HTTP {(int)response.StatusCode}.");
            }

            return ServiceResult<TData>.Success(payload.Data);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ServiceResult<TData>.Failure("Yêu cầu API đã hết thời gian chờ.");
        }
        catch (HttpRequestException)
        {
            return ServiceResult<TData>.Failure(
                "Không thể kết nối đến máy chủ. Vui lòng kiểm tra mạng.");
        }
        catch (JsonException)
        {
            return ServiceResult<TData>.Failure("Máy chủ trả về dữ liệu không hợp lệ.");
        }
        catch (Exception)
        {
            return ServiceResult<TData>.Failure("Không thể tải dữ liệu lựa chọn.");
        }
    }

    private static SaveBatchResponse? TryDeserializeSaveResponse(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<SaveBatchResponse>(body, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string GetSaveMessage(
        SaveBatchResponse? payload,
        string responseBody,
        HttpStatusCode statusCode)
    {
        if (!string.IsNullOrWhiteSpace(payload?.Message))
        {
            return payload.Message;
        }

        if (!string.IsNullOrWhiteSpace(responseBody))
        {
            var normalized = responseBody.Trim();
            return normalized.Length <= 1000 ? normalized : normalized[..1000];
        }

        return $"API trả về lỗi HTTP {(int)statusCode} ({statusCode}).";
    }

    private static int? TryGetBatchId(JsonElement? data)
    {
        if (data is not { ValueKind: JsonValueKind.Object } element)
        {
            return null;
        }

        foreach (var propertyName in new[] { "id", "batch_id" })
        {
            if (element.TryGetProperty(propertyName, out var idElement) &&
                idElement.ValueKind == JsonValueKind.Number &&
                idElement.TryGetInt32(out var id))
            {
                return id;
            }
        }

        return null;
    }
}
