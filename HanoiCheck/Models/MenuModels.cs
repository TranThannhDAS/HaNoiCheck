using System.Text.Json.Serialization;

namespace HanoiCheck.Models;

public enum MenuResolutionKind
{
    StudentGroup,
    School,
    Dish
}

public sealed class StudentGroupOption
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("nutrition_name")]
    public string? NutritionName { get; set; }

    [JsonPropertyName("school_levels")]
    public List<int> SchoolLevels { get; set; } = [];
}

public sealed class SchoolOption
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

public sealed class DishOption
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("code")]
    public string? Code { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

public sealed class SchoolOptionsData
{
    [JsonPropertyName("schools")]
    public List<SchoolOption> Schools { get; set; } = [];
}

public sealed class MenuImportItem
{
    public required string MealValue { get; init; }
    public required string DishValue { get; init; }
    public int RowNumber { get; init; }
}

public sealed class MenuImportBlock
{
    public string? SourceFileName { get; init; }
    public int RowNumber { get; init; }
    public int StartRowNumber => RowNumber;
    public string? Code { get; set; }
    public string? Name { get; set; }
    public string? StatusText { get; set; }
    public string? StudentGroupValue { get; set; }
    public string? DayValue { get; set; }
    public List<string> SchoolValues { get; } = [];
    public List<MenuImportItem> Items { get; } = [];
    public List<string> ParseErrors { get; } = [];
    public List<string> ParseWarnings { get; } = [];
}

public sealed class MenuOptionCandidate
{
    public required int Id { get; init; }
    public string? Name { get; init; }
    public string? Code { get; init; }
    public string? Metadata { get; init; }
}

public sealed class MenuResolutionIssue
{
    public required MenuResolutionKind Kind { get; init; }
    public required string SourceValue { get; init; }
    public required string DisplayName { get; init; }
    public int? StudentGroupId { get; init; }
    public List<MenuOptionCandidate> Candidates { get; init; } = [];
}

public sealed class ResolvedMenuItem
{
    public int SourceRowNumber { get; init; }
    public string? SourceMealValue { get; init; }
    public string? SourceDishValue { get; init; }
    public int? MealId { get; set; }
    public string? MealName { get; set; }
    public int? DishId { get; set; }
    public string? DishCode { get; set; }
    public string? DishName { get; set; }
}

public sealed class ResolvedMenuRow
{
    public required MenuImportBlock Source { get; init; }
    public int RowNumber => Source.RowNumber;
    public string? Code => Source.Code;
    public string? Name => Source.Name;
    public bool? Status { get; set; }
    public int? StudentGroupId { get; set; }
    public string? StudentGroupName { get; set; }
    public int? DayKey { get; set; }
    public string? DayName { get; set; }
    public List<int> SchoolIds { get; } = [];
    public List<SchoolOption> Schools { get; } = [];
    public List<ResolvedMenuItem> Items { get; } = [];
    public ImportRowStatus ValidationStatus { get; set; } = ImportRowStatus.Pending;
    public List<string> Errors { get; } = [];
    public List<string> Warnings { get; } = [];
    public List<MenuResolutionIssue> ResolutionIssues { get; } = [];
    public BatchSaveStatus SaveStatus { get; set; } = BatchSaveStatus.NotStarted;
    public SaveBatchOutcome? LastSaveOutcome { get; set; }
    public string? SaveMessage { get; set; }
    public DateTimeOffset? SavedAt { get; set; }
    public int? MenuId { get; set; }
}

public sealed class MenuValidationResult
{
    public List<ResolvedMenuRow> Rows { get; init; } = [];
    public int Total => Rows.Count;
    public int Valid => Rows.Count(row => row.ValidationStatus == ImportRowStatus.Valid);
    public int RequiresSelection => Rows.Count(row => row.ValidationStatus == ImportRowStatus.RequiresUserSelection);
    public int Errors => Rows.Count(row => row.ValidationStatus is ImportRowStatus.NotFound or ImportRowStatus.Error);
    public int Saved => Rows.Count(row => row.SaveStatus == BatchSaveStatus.Success);
}

public sealed record MenuExcelImportResult(
    bool IsSuccess,
    IReadOnlyList<MenuImportBlock> Rows,
    string? ErrorMessage)
{
    public static MenuExcelImportResult Success(IReadOnlyList<MenuImportBlock> rows) =>
        new(true, rows, null);

    public static MenuExcelImportResult Failure(string message) =>
        new(false, Array.Empty<MenuImportBlock>(), message);
}

public sealed record MenuSaveResult(
    SaveBatchOutcome Outcome,
    string Message,
    int? HttpStatusCode = null,
    int? MenuId = null)
{
    public bool IsSuccess => Outcome == SaveBatchOutcome.Success;
}

public sealed class MenuSaveProgress
{
    public int Current { get; init; }
    public int Total { get; init; }
    public required ResolvedMenuRow CurrentRow { get; init; }
    public int SuccessCount { get; init; }
    public int FailedCount { get; init; }
    public int UnknownCount { get; init; }
}

public sealed class MenuSaveSummary
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

public sealed class MenuStoreRequest
{
    [JsonPropertyName("code")]
    public required string Code { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("status")]
    public bool Status { get; init; }

    [JsonPropertyName("student_group_id")]
    public int StudentGroupId { get; init; }

    [JsonPropertyName("day_key")]
    public int DayKey { get; init; }

    [JsonPropertyName("school_ids")]
    public List<int> SchoolIds { get; init; } = [];

    [JsonPropertyName("items")]
    public List<MenuStoreItemRequest> Items { get; init; } = [];
}

public sealed class MenuStoreItemRequest
{
    [JsonPropertyName("supplier_meal_id")]
    public int SupplierMealId { get; init; }

    [JsonPropertyName("supplier_dish_id")]
    public int SupplierDishId { get; init; }
}
