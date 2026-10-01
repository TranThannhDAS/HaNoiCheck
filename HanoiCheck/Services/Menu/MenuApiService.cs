using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using HanoiCheck.Models;
using HanoiCheck.Services.Licensing;
using Microsoft.Extensions.Logging;

namespace HanoiCheck.Services.Menu;

public sealed class MenuApiService(
    HttpClient httpClient,
    AuthStateService authState,
    IDemoLicenseService demoLicense,
    ILogger<MenuApiService> logger)
    : IMenuApiService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private IReadOnlyList<StudentGroupOption>? _studentGroupsCache;
    private IReadOnlyList<SchoolOption>? _schoolsCache;
    private readonly Dictionary<int, IReadOnlyList<DishOption>> _dishOptionsCache = [];

    public async Task<ServiceResult<IReadOnlyList<StudentGroupOption>>> GetStudentGroupsAsync(
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        if (!forceRefresh && _studentGroupsCache is not null)
        {
            return ServiceResult<IReadOnlyList<StudentGroupOption>>.Success(_studentGroupsCache);
        }

        var result = await GetWrappedAsync<List<StudentGroupOption>>(
            "supplier/student-groups/all", cancellationToken);
        if (!result.IsSuccess || result.Value is null)
        {
            return ServiceResult<IReadOnlyList<StudentGroupOption>>.Failure(
                result.ErrorMessage ?? "Không thể tải danh sách nhóm tuổi.");
        }

        _studentGroupsCache = result.Value;
        return ServiceResult<IReadOnlyList<StudentGroupOption>>.Success(_studentGroupsCache);
    }

    public async Task<ServiceResult<IReadOnlyList<SchoolOption>>> GetSchoolOptionsAsync(
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        if (!forceRefresh && _schoolsCache is not null)
        {
            return ServiceResult<IReadOnlyList<SchoolOption>>.Success(_schoolsCache);
        }

        var result = await GetWrappedAsync<SchoolOptionsData>(
            "supplier/menus/school-options", cancellationToken);
        if (!result.IsSuccess || result.Value is null)
        {
            return ServiceResult<IReadOnlyList<SchoolOption>>.Failure(
                result.ErrorMessage ?? "Không thể tải danh sách trường.");
        }

        _schoolsCache = result.Value.Schools;
        return ServiceResult<IReadOnlyList<SchoolOption>>.Success(_schoolsCache);
    }

    public async Task<ServiceResult<IReadOnlyList<DishOption>>> GetDishOptionsAsync(
        int studentGroupId,
        CancellationToken cancellationToken = default)
    {
        if (_dishOptionsCache.TryGetValue(studentGroupId, out var cached))
        {
            return ServiceResult<IReadOnlyList<DishOption>>.Success(cached);
        }

        var result = await GetWrappedAsync<List<DishOption>>(
            $"supplier/menus/dish-options?student_group_id={studentGroupId}", cancellationToken);
        if (!result.IsSuccess || result.Value is null)
        {
            return ServiceResult<IReadOnlyList<DishOption>>.Failure(
                result.ErrorMessage ?? "Không thể tải danh sách món ăn.");
        }

        _dishOptionsCache[studentGroupId] = result.Value;
        return ServiceResult<IReadOnlyList<DishOption>>.Success(result.Value);
    }

    public async Task<MenuSaveResult> SaveMenuAsync(
        ResolvedMenuRow row,
        CancellationToken cancellationToken = default)
    {
        if (row.ValidationStatus != ImportRowStatus.Valid)
        {
            return new MenuSaveResult(
                SaveBatchOutcome.Failed, "Chỉ có thể lưu thực đơn đã được resolve hợp lệ.");
        }

        if (row.SaveStatus == BatchSaveStatus.Success)
        {
            return new MenuSaveResult(SaveBatchOutcome.Failed, "Thực đơn này đã được lưu trước đó.");
        }

        var license = await demoLicense.CheckAsync(false, cancellationToken);
        if (!license.CanUseApplication)
        {
            return new MenuSaveResult(
                SaveBatchOutcome.LicenseBlocked,
                license.ErrorMessage ?? "Phiên bản dùng thử đã hết hạn.");
        }

        if (string.IsNullOrWhiteSpace(authState.AccessToken))
        {
            return new MenuSaveResult(
                SaveBatchOutcome.Unauthorized, "Phiên đăng nhập không hợp lệ.", 401);
        }

        var payload = new MenuStoreRequest
        {
            Code = row.Code!,
            Name = row.Name!,
            Status = row.Status!.Value,
            StudentGroupId = row.StudentGroupId!.Value,
            DayKey = row.DayKey!.Value,
            SchoolIds = [.. row.SchoolIds],
            Items = row.Items.Select(item => new MenuStoreItemRequest
            {
                SupplierMealId = item.MealId!.Value,
                SupplierDishId = item.DishId!.Value
            }).ToList()
        };

        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "supplier/menus/store")
            {
                Content = JsonContent.Create(payload, options: JsonOptions)
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authState.AccessToken);
            using var response = await httpClient.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var responsePayload = TryDeserialize(body);
            var message = GetMessage(responsePayload, body, response.StatusCode);

            logger.LogInformation(
                "Save menu row {RowNumber}, code {MenuCode}: HTTP {StatusCode}, duration {DurationMs} ms",
                row.RowNumber, row.Code, (int)response.StatusCode, stopwatch.ElapsedMilliseconds);

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                ClearCache();
                authState.Clear();
                return new MenuSaveResult(
                    SaveBatchOutcome.Unauthorized,
                    "Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại.",
                    (int)response.StatusCode);
            }

            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                return new MenuSaveResult(
                    SaveBatchOutcome.Forbidden,
                    responsePayload?.Message ?? "Bạn không có quyền lưu thực đơn.",
                    (int)response.StatusCode);
            }

            if (!response.IsSuccessStatusCode || responsePayload is { Success: false })
            {
                return new MenuSaveResult(
                    SaveBatchOutcome.Failed, message, (int)response.StatusCode);
            }

            return new MenuSaveResult(
                SaveBatchOutcome.Success,
                responsePayload?.Message ?? "Lưu thực đơn thành công.",
                (int)response.StatusCode,
                TryGetId(responsePayload?.Data));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new MenuSaveResult(
                SaveBatchOutcome.Cancelled,
                "Yêu cầu đã bị hủy; chưa thể xác định thực đơn hiện tại đã được tạo hay chưa.");
        }
        catch (OperationCanceledException)
        {
            return new MenuSaveResult(
                SaveBatchOutcome.Unknown,
                "Yêu cầu hết thời gian chờ; chưa thể xác định thực đơn đã được tạo hay chưa.");
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Network error while saving menu row {RowNumber}", row.RowNumber);
            return new MenuSaveResult(
                SaveBatchOutcome.Unknown,
                "Mất kết nối khi lưu; chưa thể xác định thực đơn đã được tạo hay chưa.");
        }
    }

    public void ClearCache()
    {
        _studentGroupsCache = null;
        _schoolsCache = null;
        _dishOptionsCache.Clear();
    }

    private async Task<ServiceResult<T>> GetWrappedAsync<T>(
        string path,
        CancellationToken cancellationToken)
    {
        var response = await SendGetAsync(path, cancellationToken);
        if (!response.Result.IsSuccess || response.Response is null)
        {
            return ServiceResult<T>.Failure(response.Result.ErrorMessage!);
        }

        using (response.Response)
        {
            try
            {
                await using var stream = await response.Response.Content.ReadAsStreamAsync(cancellationToken);
                var payload = await JsonSerializer.DeserializeAsync<ApiResponse<T>>(
                    stream, JsonOptions, cancellationToken);
                return payload is { Success: true, Data: not null }
                    ? ServiceResult<T>.Success(payload.Data)
                    : ServiceResult<T>.Failure(payload?.Message ?? "API trả về dữ liệu không hợp lệ.");
            }
            catch (JsonException)
            {
                return ServiceResult<T>.Failure("Máy chủ trả về dữ liệu không hợp lệ.");
            }
        }
    }

    private async Task<(ServiceResult<bool> Result, HttpResponseMessage? Response)> SendGetAsync(
        string path,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(authState.AccessToken))
        {
            return (ServiceResult<bool>.Failure("Phiên đăng nhập không hợp lệ."), null);
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authState.AccessToken);
            var response = await httpClient.SendAsync(request, cancellationToken);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                response.Dispose();
                ClearCache();
                authState.Clear();
                return (ServiceResult<bool>.Failure(
                    "Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại."), null);
            }

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                response.Dispose();
                return (ServiceResult<bool>.Failure(
                    string.IsNullOrWhiteSpace(body)
                        ? $"API trả về lỗi HTTP {(int)response.StatusCode}."
                        : body[..Math.Min(body.Length, 1000)]), null);
            }

            return (ServiceResult<bool>.Success(true), response);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return (ServiceResult<bool>.Failure("Yêu cầu API đã hết thời gian chờ."), null);
        }
        catch (HttpRequestException)
        {
            return (ServiceResult<bool>.Failure(
                "Không thể kết nối đến máy chủ. Vui lòng kiểm tra mạng."), null);
        }
    }

    private static SaveBatchResponse? TryDeserialize(string body)
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

    private static string GetMessage(
        SaveBatchResponse? payload,
        string body,
        HttpStatusCode statusCode)
    {
        if (!string.IsNullOrWhiteSpace(payload?.Message))
        {
            return payload.Message;
        }

        return string.IsNullOrWhiteSpace(body)
            ? $"API trả về lỗi HTTP {(int)statusCode} ({statusCode})."
            : body.Trim()[..Math.Min(body.Trim().Length, 1000)];
    }

    private static int? TryGetId(JsonElement? data)
    {
        if (data is not { ValueKind: JsonValueKind.Object } element)
        {
            return null;
        }

        foreach (var key in new[] { "id", "menu_id" })
        {
            if (element.TryGetProperty(key, out var idElement) && idElement.TryGetInt32(out var id))
            {
                return id;
            }
        }

        return null;
    }
}
