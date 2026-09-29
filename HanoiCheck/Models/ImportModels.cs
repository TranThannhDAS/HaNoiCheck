namespace HanoiCheck.Models;

public enum ImportRowStatus
{
    Pending,
    Valid,
    RequiresUserSelection,
    NotFound,
    Error
}

public enum BatchSaveStatus
{
    NotStarted,
    Saving,
    Success,
    Failed,
    Unknown
}

public enum SaveBatchOutcome
{
    Success,
    Failed,
    Unknown,
    Unauthorized,
    Forbidden,
    LicenseBlocked,
    Cancelled
}

public enum OptionKind
{
    Food,
    Warehouse,
    SubSupplier,
    Employee
}

public sealed class ImportBatchRow
{
    public string? SourceFileName { get; init; }
    public int RowNumber { get; init; }
    public string? FoodCode { get; init; }
    public string? FoodName { get; init; }
    public string? Code { get; init; }
    public string? Name { get; init; }
    public DateTime? ImportDate { get; init; }
    public DateTime? ExpireDate { get; init; }
    public string? WarehouseCode { get; init; }
    public string? WarehouseName { get; init; }
    public string? SubSupplierCode { get; init; }
    public string? SubSupplierName { get; init; }
    public string? EmployeeCode { get; init; }
    public string? EmployeeName { get; init; }
    public string? Note { get; init; }
    public string? ImageSource { get; init; }
    public string? SourceDirectory { get; init; }
}

public sealed class ResolvedOption
{
    public required int Id { get; init; }
    public string? Code { get; init; }
    public string? Name { get; init; }
    public string? Metadata { get; init; }
}

public sealed class OptionResolutionIssue
{
    public required OptionKind Kind { get; init; }
    public required string SourceValue { get; init; }
    public required string DisplayName { get; init; }
    public List<ResolvedOption> Candidates { get; init; } = [];
}

public sealed class ResolvedImage
{
    public required string FilePath { get; init; }
    public required string Name { get; init; }
    public required long Size { get; init; }
}

public sealed class BatchStepModel
{
    public int SupplierStepId { get; init; }
    public string? Name { get; init; }
    public string? Code { get; init; }
    public int Order { get; init; }
    public List<int> SupplierEmployeeIds { get; init; } = [];
    public int? SupplierFacilityId { get; init; }
    public string? Address { get; init; }
    public string? Note { get; init; }
    public List<ResolvedImage> Images { get; init; } = [];
}

public sealed class ResolvedBatchRow
{
    public required ImportBatchRow Source { get; init; }
    public int RowNumber => Source.RowNumber;
    public int? SupplierFoodId { get; set; }
    public string? FoodCode { get; set; }
    public string? FoodName { get; set; }
    public string? Code => Source.Code;
    public string? Name => Source.Name;
    public DateTime? ImportDate => Source.ImportDate;
    public DateTime? ExpireDate => Source.ExpireDate;
    public DateTime? ProductionDate { get; set; }
    public string? PurchaseAddress { get; set; }
    public string? Description { get; set; }
    public List<int> SupplierWarehouseIds { get; } = [];
    public string? WarehouseDisplay { get; set; }
    public List<int> SupplierSubSupplierIds { get; } = [];
    public string? SubSupplierDisplay { get; set; }
    public int? SupplierEmployeeId { get; set; }
    public string? EmployeeDisplay { get; set; }
    public int? DefaultProcessId { get; set; }
    public string? ProcessName { get; set; }
    public List<ResolvedImage> Images { get; } = [];
    public List<BatchStepModel> BatchSteps { get; } = [];
    public ImportRowStatus Status { get; set; } = ImportRowStatus.Pending;
    public List<string> Errors { get; } = [];
    public List<string> Warnings { get; } = [];
    public List<OptionResolutionIssue> ResolutionIssues { get; } = [];
    public BatchSaveStatus SaveStatus { get; set; } = BatchSaveStatus.NotStarted;
    public SaveBatchOutcome? LastSaveOutcome { get; set; }
    public string? SaveMessage { get; set; }
    public DateTimeOffset? SavedAt { get; set; }
    public int? BatchId { get; set; }
}

public sealed class ImportValidationResult
{
    public List<ResolvedBatchRow> Rows { get; init; } = [];
    public int Total => Rows.Count;
    public int Valid => Rows.Count(row => row.Status == ImportRowStatus.Valid);
    public int RequiresSelection => Rows.Count(row => row.Status == ImportRowStatus.RequiresUserSelection);
    public int Errors => Rows.Count(row => row.Status is ImportRowStatus.NotFound or ImportRowStatus.Error);
}

public sealed record ExcelImportResult(
    bool IsSuccess,
    IReadOnlyList<ImportBatchRow> Rows,
    string? ErrorMessage)
{
    public static ExcelImportResult Success(IReadOnlyList<ImportBatchRow> rows) =>
        new(true, rows, null);

    public static ExcelImportResult Failure(string message) =>
        new(false, Array.Empty<ImportBatchRow>(), message);
}

public sealed record SaveBatchResult(
    SaveBatchOutcome Outcome,
    string Message,
    int? HttpStatusCode = null,
    int? BatchId = null)
{
    public bool IsSuccess => Outcome == SaveBatchOutcome.Success;
}

public sealed class BatchSaveProgress
{
    public int Current { get; init; }
    public int Total { get; init; }
    public required ResolvedBatchRow CurrentRow { get; init; }
    public int SuccessCount { get; init; }
    public int FailedCount { get; init; }
    public int UnknownCount { get; init; }
}

public sealed class BatchSaveSummary
{
    public int Total { get; init; }
    public int Processed { get; set; }
    public int SuccessCount { get; set; }
    public int FailedCount { get; set; }
    public int UnknownCount { get; set; }
    public bool WasCancelled { get; set; }
    public SaveBatchOutcome? StopReason { get; set; }
    public string? Message { get; set; }
}
