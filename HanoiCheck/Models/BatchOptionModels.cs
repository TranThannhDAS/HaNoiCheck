using System.Text.Json.Serialization;

namespace HanoiCheck.Models;

public class NamedOption
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("code")]
    public string? Code { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

public sealed class FoodOption : NamedOption
{
    [JsonPropertyName("food_category_id")]
    public int? FoodCategoryId { get; set; }

    [JsonPropertyName("supplier_process_id")]
    public int? SupplierProcessId { get; set; }
}

public sealed class WarehouseOption : NamedOption;

public sealed class SubSupplierOption : NamedOption
{
    [JsonPropertyName("contract_expiry_date")]
    public string? ContractExpiryDate { get; set; }

    [JsonPropertyName("contract_expired")]
    public bool ContractExpired { get; set; }

    [JsonPropertyName("warning")]
    public string? Warning { get; set; }
}

public sealed class EmployeeOption : NamedOption
{
    [JsonPropertyName("position")]
    public string? Position { get; set; }

    [JsonPropertyName("health_certificate_expiry_date")]
    public string? HealthCertificateExpiryDate { get; set; }

    [JsonPropertyName("health_certificate_expired")]
    public bool HealthCertificateExpired { get; set; }

    [JsonPropertyName("warning")]
    public string? Warning { get; set; }
}

public sealed class FacilityOption : NamedOption;

public sealed class ProcessOption : NamedOption;

public sealed class FormOptionsData
{
    [JsonPropertyName("default_process_id")]
    public int? DefaultProcessId { get; set; }

    [JsonPropertyName("default_process")]
    public ProcessOption? DefaultProcess { get; set; }

    [JsonPropertyName("sub_suppliers")]
    public List<SubSupplierOption> SubSuppliers { get; set; } = [];

    [JsonPropertyName("foods")]
    public List<FoodOption> Foods { get; set; } = [];

    [JsonPropertyName("warehouses")]
    public List<WarehouseOption> Warehouses { get; set; } = [];

    [JsonPropertyName("facilities")]
    public List<FacilityOption> Facilities { get; set; } = [];

    [JsonPropertyName("employees")]
    public List<EmployeeOption> Employees { get; set; } = [];
}

public sealed class FormOptionsResponse : ApiResponse<FormOptionsData>;

public sealed class FoodOptionsData
{
    [JsonPropertyName("default_process_id")]
    public int? DefaultProcessId { get; set; }

    [JsonPropertyName("default_process")]
    public ProcessOption? DefaultProcess { get; set; }

    [JsonPropertyName("sub_suppliers")]
    public List<SubSupplierOption> SubSuppliers { get; set; } = [];
}

public sealed class FoodOptionsResponse : ApiResponse<FoodOptionsData>;

public sealed class ProcessStepOption
{
    [JsonPropertyName("supplier_step_id")]
    public int SupplierStepId { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("code")]
    public string? Code { get; set; }

    [JsonPropertyName("order")]
    public int Order { get; set; }
}

public sealed class ProcessDetailData : NamedOption
{
    [JsonPropertyName("step_number")]
    public int StepNumber { get; set; }

    [JsonPropertyName("steps")]
    public List<ProcessStepOption> Steps { get; set; } = [];
}

public sealed class ProcessDetailResponse : ApiResponse<ProcessDetailData>;
