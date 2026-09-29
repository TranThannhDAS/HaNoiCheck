using HanoiCheck.Models;

namespace HanoiCheck.Services;

public sealed class OptionResolverService(
    IBatchApiService batchApi,
    ImageResolverService imageResolver)
{
    private readonly Dictionary<(OptionKind Kind, string Value), int> _sessionMappings = [];

    public void ResetSessionMappings() => _sessionMappings.Clear();

    public void RememberSelection(OptionKind kind, string sourceValue, int selectedId)
    {
        _sessionMappings[(kind, Normalize(sourceValue))] = selectedId;
    }

    public async Task<ImportValidationResult> ResolveAsync(
        IReadOnlyCollection<ImportBatchRow> sourceRows,
        CancellationToken cancellationToken = default)
    {
        var optionsResult = await batchApi.GetFormOptionsAsync(false, cancellationToken);
        if (!optionsResult.IsSuccess || optionsResult.Value is null)
        {
            return new ImportValidationResult
            {
                Rows = sourceRows.Select(source => CreateApiErrorRow(
                    source, optionsResult.ErrorMessage ?? "Không thể tải dữ liệu lựa chọn.")).ToList()
            };
        }

        var rows = new List<ResolvedBatchRow>(sourceRows.Count);
        foreach (var source in sourceRows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            rows.Add(await ResolveRowAsync(source, optionsResult.Value, cancellationToken));
        }

        return new ImportValidationResult { Rows = rows };
    }

    private async Task<ResolvedBatchRow> ResolveRowAsync(
        ImportBatchRow source,
        FormOptionsData options,
        CancellationToken cancellationToken)
    {
        var row = new ResolvedBatchRow { Source = source };
        ValidateRequiredValues(row);

        var foodMatch = ResolveOption(
            source.FoodCode, source.FoodName, options.Foods,
            OptionKind.Food, "thực phẩm", FoodMetadata);
        ApplyMatch(row, foodMatch, option =>
        {
            row.SupplierFoodId = option.Id;
            row.FoodCode = option.Code;
            row.FoodName = option.Name;
        });

        ResolveOptionalOption(
            row, source.WarehouseCode, source.WarehouseName, options.Warehouses,
            OptionKind.Warehouse, "kho", null,
            option =>
            {
                row.SupplierWarehouseIds.Add(option.Id);
                row.WarehouseDisplay = Display(option);
            });

        ResolveOptionalOption(
            row, source.SubSupplierCode, source.SubSupplierName, options.SubSuppliers,
            OptionKind.SubSupplier, "nhà cung cấp", SubSupplierMetadata,
            option =>
            {
                row.SupplierSubSupplierIds.Add(option.Id);
                row.SubSupplierDisplay = Display(option);
            });

        ResolveOptionalOption(
            row, source.EmployeeCode, source.EmployeeName, options.Employees,
            OptionKind.Employee, "nhân viên", EmployeeMetadata,
            option =>
            {
                row.SupplierEmployeeId = option.Id;
                row.EmployeeDisplay = Display(option);
            });

        if (row.SupplierFoodId is int foodId)
        {
            await ResolveProcessAsync(row, foodId, cancellationToken);
        }

        ResolveImages(row);
        BuildBatchSteps(row);
        SetStatus(row);
        return row;
    }

    private async Task ResolveProcessAsync(
        ResolvedBatchRow row,
        int foodId,
        CancellationToken cancellationToken)
    {
        var foodOptionsResult = await batchApi.GetFoodOptionsAsync(foodId, cancellationToken);
        if (!foodOptionsResult.IsSuccess || foodOptionsResult.Value is null)
        {
            row.Errors.Add(foodOptionsResult.ErrorMessage ?? "Không thể tải options của thực phẩm.");
            return;
        }

        var processId = foodOptionsResult.Value.DefaultProcessId;
        if (processId is null or <= 0)
        {
            row.Errors.Add("Thực phẩm không có quy trình mặc định.");
            return;
        }

        row.DefaultProcessId = processId;
        row.ProcessName = foodOptionsResult.Value.DefaultProcess?.Name;
        var processResult = await batchApi.GetProcessAsync(processId.Value, cancellationToken);
        if (!processResult.IsSuccess || processResult.Value is null)
        {
            row.Errors.Add(processResult.ErrorMessage ?? "Không thể tải chi tiết quy trình.");
            return;
        }

        row.ProcessName = processResult.Value.Name ?? row.ProcessName;
        if (processResult.Value.Steps.Count == 0)
        {
            row.Errors.Add("Quy trình không có bước xử lý.");
            return;
        }

        foreach (var step in processResult.Value.Steps.OrderBy(step => step.Order))
        {
            row.BatchSteps.Add(new BatchStepModel
            {
                SupplierStepId = step.SupplierStepId,
                Name = step.Name,
                Code = step.Code,
                Order = step.Order,
                Note = row.Source.Note
            });
        }
    }

    private static void ValidateRequiredValues(ResolvedBatchRow row)
    {
        if (string.IsNullOrWhiteSpace(row.Source.FoodName) &&
            string.IsNullOrWhiteSpace(row.Source.FoodCode))
        {
            row.Errors.Add("Thiếu tên hoặc mã thực phẩm.");
        }

        if (string.IsNullOrWhiteSpace(row.Source.Code))
        {
            row.Errors.Add("Thiếu mã lô.");
        }

        if (string.IsNullOrWhiteSpace(row.Source.Name))
        {
            row.Errors.Add("Thiếu tên lô.");
        }

        if (row.Source.ImportDate is null)
        {
            row.Errors.Add("Ngày nhập trống hoặc không đúng định dạng.");
        }
    }

    private void ResolveImages(ResolvedBatchRow row)
    {
        if (string.IsNullOrWhiteSpace(row.Source.ImageSource))
        {
            return;
        }

        var result = imageResolver.ResolveFromExcel(
            row.Source.ImageSource, row.Source.SourceDirectory);
        if (!result.IsSuccess || result.Value is null)
        {
            row.Errors.Add(result.ErrorMessage ?? "Không thể resolve ảnh.");
            return;
        }

        row.Images.AddRange(result.Value);
    }

    private static void BuildBatchSteps(ResolvedBatchRow row)
    {
        foreach (var step in row.BatchSteps)
        {
            if (row.SupplierEmployeeId is int employeeId)
            {
                step.SupplierEmployeeIds.Add(employeeId);
            }
        }

    }

    private static void SetStatus(ResolvedBatchRow row)
    {
        if (row.Errors.Count > 0)
        {
            row.Status = row.Errors.Any(error => error.Contains("Không tìm thấy", StringComparison.OrdinalIgnoreCase))
                ? ImportRowStatus.NotFound
                : ImportRowStatus.Error;
            return;
        }

        row.Status = row.ResolutionIssues.Count > 0
            ? ImportRowStatus.RequiresUserSelection
            : ImportRowStatus.Valid;
    }

    private void ResolveOptionalOption<T>(
        ResolvedBatchRow row,
        string? code,
        string? name,
        IReadOnlyCollection<T> options,
        OptionKind kind,
        string label,
        Func<T, string?>? metadata,
        Action<T> onResolved)
        where T : NamedOption
    {
        if (string.IsNullOrWhiteSpace(code) && string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        ApplyMatch(row, ResolveOption(code, name, options, kind, label, metadata), onResolved);
    }

    private OptionMatch<T> ResolveOption<T>(
        string? code,
        string? name,
        IReadOnlyCollection<T> options,
        OptionKind kind,
        string label,
        Func<T, string?>? metadata)
        where T : NamedOption
    {
        var sourceValue = !string.IsNullOrWhiteSpace(code) ? code.Trim() : name?.Trim() ?? string.Empty;
        if (_sessionMappings.TryGetValue((kind, Normalize(sourceValue)), out var selectedId))
        {
            var remembered = options.FirstOrDefault(option => option.Id == selectedId);
            if (remembered is not null)
            {
                return OptionMatch<T>.Resolved(remembered);
            }
        }

        if (!string.IsNullOrWhiteSpace(code))
        {
            var codeMatches = options.Where(option => Equal(option.Code, code)).ToList();
            if (codeMatches.Count == 1)
            {
                return OptionMatch<T>.Resolved(codeMatches[0]);
            }

            if (codeMatches.Count > 1)
            {
                return OptionMatch<T>.Ambiguous(CreateIssue(kind, code.Trim(), label, codeMatches, metadata));
            }
        }

        if (!string.IsNullOrWhiteSpace(name))
        {
            if (_sessionMappings.TryGetValue((kind, Normalize(name)), out selectedId))
            {
                var rememberedByName = options.FirstOrDefault(option => option.Id == selectedId);
                if (rememberedByName is not null)
                {
                    return OptionMatch<T>.Resolved(rememberedByName);
                }
            }

            var nameMatches = options.Where(option => Equal(option.Name, name)).ToList();
            if (nameMatches.Count == 1)
            {
                return OptionMatch<T>.Resolved(nameMatches[0]);
            }

            if (nameMatches.Count > 1)
            {
                return OptionMatch<T>.Ambiguous(CreateIssue(kind, name.Trim(), label, nameMatches, metadata));
            }
        }

        return OptionMatch<T>.NotFound(
            $"Không tìm thấy {label} '{name ?? code ?? string.Empty}'.");
    }

    private static OptionResolutionIssue CreateIssue<T>(
        OptionKind kind,
        string sourceValue,
        string label,
        IEnumerable<T> matches,
        Func<T, string?>? metadata)
        where T : NamedOption => new()
        {
            Kind = kind,
            SourceValue = sourceValue,
            DisplayName = char.ToUpperInvariant(label[0]) + label[1..],
            Candidates = matches.Select(option => new ResolvedOption
            {
                Id = option.Id,
                Code = option.Code,
                Name = option.Name,
                Metadata = metadata?.Invoke(option)
            }).ToList()
        };

    private static void ApplyMatch<T>(
        ResolvedBatchRow row,
        OptionMatch<T> match,
        Action<T> onResolved)
        where T : NamedOption
    {
        if (match.Value is not null)
        {
            onResolved(match.Value);
        }
        else if (match.Issue is not null)
        {
            row.ResolutionIssues.Add(match.Issue);
        }
        else if (match.ErrorMessage is not null)
        {
            row.Errors.Add(match.ErrorMessage);
        }
    }

    private static ResolvedBatchRow CreateApiErrorRow(ImportBatchRow source, string error)
    {
        var row = new ResolvedBatchRow { Source = source, Status = ImportRowStatus.Error };
        row.Errors.Add(error);
        return row;
    }

    private static bool Equal(string? left, string? right) =>
        string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string value) => value.Trim().ToUpperInvariant();

    private static string Display(NamedOption option) =>
        string.IsNullOrWhiteSpace(option.Code)
            ? option.Name ?? string.Empty
            : $"{option.Name} ({option.Code})";

    private static string? FoodMetadata(FoodOption option) =>
        option.FoodCategoryId is null ? null : $"Nhóm thực phẩm: {option.FoodCategoryId}";

    private static string? EmployeeMetadata(EmployeeOption option) =>
        string.IsNullOrWhiteSpace(option.Position) ? null : $"Chức vụ: {option.Position}";

    private static string? SubSupplierMetadata(SubSupplierOption option) =>
        option.ContractExpired ? "Hợp đồng đã hết hạn" : option.Warning;

    private sealed record OptionMatch<T>(T? Value, OptionResolutionIssue? Issue, string? ErrorMessage)
        where T : NamedOption
    {
        public static OptionMatch<T> Resolved(T value) => new(value, null, null);
        public static OptionMatch<T> Ambiguous(OptionResolutionIssue issue) => new(null, issue, null);
        public static OptionMatch<T> NotFound(string error) => new(null, null, error);
    }
}
