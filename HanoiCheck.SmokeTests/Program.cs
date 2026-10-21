using HanoiCheck.Models;
using HanoiCheck.Services;
using HanoiCheck.Services.Menu;

var templatePath = args.FirstOrDefault() ??
    @"C:\Users\datvc\Downloads\IMPORT_THUC_DON_TEMPLATE.xlsx";

var parser = new MenuExcelImportService();
var template = await parser.ReadAsync(templatePath);
Assert(template.IsSuccess, template.ErrorMessage ?? "Không đọc được template.");
Assert(template.Rows.Count == 2, "Template phải tạo đúng 2 MenuImportBlock.");
Assert(template.Rows[0].RowNumber == 2 && template.Rows[0].Items.Count == 4,
    "TD001 phải bắt đầu ở dòng 2 và có 4 món.");
Assert(template.Rows[1].RowNumber == 6 && template.Rows[1].Items.Count == 2,
    "TD002 phải bắt đầu ở dòng 6 và có 2 món.");
Assert(template.Rows[0].Items.Count(item => item.MealValue == "Sáng") == 2,
    "TD001 phải giữ nhiều món trong cùng bữa Sáng.");
Assert(template.Rows[0].SchoolValues.SequenceEqual(["Trường A", "Trường B"]),
    "Danh sách trường phải được tách đúng bằng dấu chấm phẩy.");

var orphan = MenuExcelImportService.ParseRows([
    new MenuExcelDataRow(2, null, null, null, null, null, null, "Sáng", "Bánh mì")
]);
Assert(orphan.Count == 1 && orphan[0].ParseErrors.Any(error => error.Contains("chưa thuộc thực đơn nào")),
    "Dòng món không có parent phải được báo lỗi parser.");

var fakeApi = new FakeMenuApiService();
var resolver = new MenuResolverService(fakeApi);
var ambiguousBlock = MenuExcelImportService.ParseRows([
    new MenuExcelDataRow(2, "TD-A", "Menu A", "Đang áp dụng", "Nhóm A", "Thứ 4", "Trường A", "Sáng", "Bánh mì")
]);
var ambiguous = await resolver.ResolveAsync(ambiguousBlock);
var issue = ambiguous.Value!.Rows.Single().ResolutionIssues.Single(item => item.Kind == MenuResolutionKind.Dish);
Assert(issue.Candidates.Count == 2, "Tên món trùng phải yêu cầu người dùng chọn.");
resolver.RememberSelection(issue, 11);
var selected = await resolver.ResolveAsync(ambiguousBlock);
Assert(selected.Value!.Rows.Single().ValidationStatus == ImportRowStatus.Valid &&
       selected.Value.Rows.Single().Items.Single().DishId == 11,
    "Lựa chọn món phải được nhớ trong đúng nhóm tuổi.");

var otherGroupBlock = MenuExcelImportService.ParseRows([
    new MenuExcelDataRow(3, "TD-B", "Menu B", "Đang áp dụng", "Nhóm B", "Thứ 5", "Trường A", "Sáng", "Bánh mì")
]);
var otherGroup = await resolver.ResolveAsync(otherGroupBlock);
Assert(otherGroup.Value!.Rows.Single().ResolutionIssues.Any(item => item.Kind == MenuResolutionKind.Dish),
    "Mapping món của nhóm A không được áp dụng sang nhóm B.");

var batchTemplatePath = args.Skip(1).FirstOrDefault() ??
    @"C:\Users\datvc\Downloads\MẪU IMPORT LÔ NHẬP HÀNG - FINAL - 01.11.2026 (1).xlsx";
var batchTemplate = await new ExcelImportService().ReadAsync(batchTemplatePath);
Assert(batchTemplate.IsSuccess, batchTemplate.ErrorMessage ?? "Không đọc được template lô nhập hàng.");
var templateRow = batchTemplate.Rows.First();
Assert(templateRow.RowNumber == 3, "Dữ liệu lô phải bắt đầu từ dòng 3 của Sheet1.");
Assert(templateRow.ProductionDate is not null &&
       !string.IsNullOrWhiteSpace(templateRow.PurchaseAddress) &&
       !string.IsNullOrWhiteSpace(templateRow.Description) &&
       !string.IsNullOrWhiteSpace(templateRow.Facility) &&
       !string.IsNullOrWhiteSpace(templateRow.ExecutionAddress),
    "Parser phải đọc các cột mới trong template A:P.");

var testDirectory = Path.Combine(Path.GetTempPath(), $"hanoicheck-smoke-{Guid.NewGuid():N}");
Directory.CreateDirectory(testDirectory);
try
{
    var batchImage = Path.Combine(testDirectory, "batch.jpg");
    var proofImage = Path.Combine(testDirectory, "proof.png");
    await File.WriteAllBytesAsync(batchImage, [1, 2, 3]);
    await File.WriteAllBytesAsync(proofImage, [4, 5, 6]);

    var proofDirectory = Path.Combine(testDirectory, "proofs");
    Directory.CreateDirectory(proofDirectory);
    await File.WriteAllBytesAsync(Path.Combine(proofDirectory, "a.jpg"), [1]);
    await File.WriteAllBytesAsync(Path.Combine(proofDirectory, "b.png"), [2]);
    await File.WriteAllBytesAsync(Path.Combine(proofDirectory, "c.pdf"), [3]);

    var full = await ResolveBatchAsync(NewBatchRow(
        facility: "CS01", imageSource: batchImage, proofSource: proofImage));
    Assert(full.Status == ImportRowStatus.Valid, "Dòng đầy đủ phải hợp lệ.");
    Assert(full.BatchSteps.Count == 2, "Phải giữ nguyên toàn bộ bước quy trình từ API.");
    Assert(full.BatchSteps[0].SupplierFacilityId == 47 &&
           full.BatchSteps[0].SupplierEmployeeIds.SequenceEqual([31]) &&
           full.BatchSteps[0].Address == "Địa chỉ thực hiện" &&
           full.BatchSteps[0].Note == "Thông tin sản xuất" &&
           full.BatchSteps[0].Images.Count == 1,
        "Dữ liệu Excel phải chỉ gắn vào bước quy trình đầu tiên.");
    Assert(full.BatchSteps[1].SupplierEmployeeIds.Count == 0 &&
           full.BatchSteps[1].SupplierFacilityId is null &&
           full.BatchSteps[1].Images.Count == 0 &&
           full.BatchSteps[1].Note is null,
        "Các bước sau phải giữ dữ liệu gốc từ API, không sao chép dữ liệu Excel.");

    var byName = await ResolveBatchAsync(NewBatchRow(facility: "Cơ sở Một"));
    Assert(byName.BatchSteps[0].SupplierFacilityId == 47,
        "Cơ sở phải resolve được bằng tên chính xác.");

    var missingFacility = await ResolveBatchAsync(NewBatchRow(facility: "Không tồn tại"));
    Assert(missingFacility.Errors.Contains("Không tìm thấy cơ sở thực hiện: Không tồn tại."),
        "Cơ sở không tồn tại phải có đúng thông báo lỗi.");

    var futureProduction = await ResolveBatchAsync(NewBatchRow(
        facility: null, productionDate: DateTime.Today.AddDays(1)));
    Assert(futureProduction.Errors.Contains("Ngày sản xuất không được lớn hơn ngày hiện tại."),
        "Ngày sản xuất trong tương lai phải bị chặn.");

    var blankOptional = await ResolveBatchAsync(NewBatchRow(
        facility: null, includeOptionalValues: false));
    Assert(blankOptional.Status == ImportRowStatus.Valid,
        "Các trường tùy chọn để trống vẫn phải hợp lệ.");

    var threeProofs = await ResolveBatchAsync(NewBatchRow(
        facility: null, proofSource: proofDirectory));
    Assert(threeProofs.Status == ImportRowStatus.Valid &&
           threeProofs.BatchSteps[0].Images.Count == 3,
        "Thư mục có 3 File chứng minh hợp lệ phải được chấp nhận.");
    using (var threeProofsMultipart = new BatchPayloadBuilder().BuildMultipartContent(threeProofs))
    {
        var threeProofPartNames = threeProofsMultipart.Select(part =>
            part.Headers.ContentDisposition?.Name?.Trim('"') ?? string.Empty).ToHashSet();
        Assert(Enumerable.Range(0, 3).All(index =>
                threeProofPartNames.Contains($"batch_steps[0][images][{index}][file]") &&
                threeProofPartNames.Contains($"batch_steps[0][images][{index}][name]")),
            "Multipart phải build đủ cả 3 File chứng minh trong thư mục.");
    }

    var invalidProof = await ResolveBatchAsync(NewBatchRow(
        facility: null, proofSource: Path.Combine(testDirectory, "missing.png")));
    Assert(invalidProof.Status == ImportRowStatus.NotFound &&
           invalidProof.Errors.Any(error => error.Contains("File chứng minh", StringComparison.OrdinalIgnoreCase)),
        "Đường dẫn File chứng minh không hợp lệ phải báo lỗi.");

    using var multipart = new BatchPayloadBuilder().BuildMultipartContent(full);
    var partNames = multipart.Select(part =>
        part.Headers.ContentDisposition?.Name?.Trim('"') ?? string.Empty).ToHashSet();
    Assert(partNames.Contains("production_date") &&
           partNames.Contains("purchase_address") &&
           partNames.Contains("description") &&
           partNames.Contains("batch_steps[0][supplier_facility_id]") &&
           partNames.Contains("batch_steps[0][address]") &&
           partNames.Contains("batch_steps[0][note]") &&
           partNames.Contains("batch_steps[0][images][0][file]") &&
           partNames.Contains("batch_steps[0][images][0][name]") &&
           partNames.Contains("images[0][file]"),
        "Multipart payload phải chứa đầy đủ trường lô, cơ sở, địa chỉ và hai nhóm file.");

    var noStepProof = await ResolveBatchAsync(
        NewBatchRow(facility: null, proofSource: proofImage), noProcessSteps: true);
    Assert(noStepProof.Errors.Contains("Không có bước quy trình để gắn File chứng minh."),
        "File chứng minh không có bước quy trình phải báo lỗi rõ ràng.");
}
finally
{
    if (Directory.Exists(testDirectory))
    {
        Directory.Delete(testDirectory, true);
    }
}

Console.WriteLine("PASS: menu import and batch template/parser/resolver/proof-file/multipart scenarios.");

static async Task<ResolvedBatchRow> ResolveBatchAsync(
    ImportBatchRow row,
    bool noProcessSteps = false)
{
    var resolver = new OptionResolverService(
        new FakeBatchApiService(noProcessSteps), new ImageResolverService());
    return (await resolver.ResolveAsync([row])).Rows.Single();
}

static ImportBatchRow NewBatchRow(
    string? facility,
    DateTime? productionDate = null,
    string? imageSource = null,
    string? proofSource = null,
    bool includeOptionalValues = true) => new()
{
    SourceFileName = "smoke.xlsx",
    SourceDirectory = Path.GetDirectoryName(imageSource ?? proofSource) ?? Path.GetTempPath(),
    RowNumber = 3,
    FoodCode = "TP01",
    FoodName = "Thực phẩm Một",
    Code = "LO01",
    Name = "Lô Một",
    ImportDate = DateTime.Today,
    ProductionDate = productionDate ?? DateTime.Today,
    ExpireDate = DateTime.Today.AddDays(1),
    WarehouseCode = includeOptionalValues ? "KHO01" : null,
    SubSupplierCode = includeOptionalValues ? "NCC01" : null,
    EmployeeCode = includeOptionalValues ? "NV01" : null,
    PurchaseAddress = includeOptionalValues ? "Địa chỉ thu mua" : null,
    Description = includeOptionalValues ? "Mô tả" : null,
    Facility = facility,
    Note = includeOptionalValues ? "Thông tin sản xuất" : null,
    ExecutionAddress = includeOptionalValues ? "Địa chỉ thực hiện" : null,
    ImageSource = imageSource,
    ProofFileSource = proofSource
};

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

file sealed class FakeMenuApiService : IMenuApiService
{
    private readonly List<StudentGroupOption> _groups =
    [
        new() { Id = 1, Name = "Nhóm A", NutritionName = "Nhóm A" },
        new() { Id = 2, Name = "Nhóm B", NutritionName = "Nhóm B" }
    ];

    private readonly List<SchoolOption> _schools = [new() { Id = 100, Name = "Trường A" }];

    public Task<ServiceResult<IReadOnlyList<StudentGroupOption>>> GetStudentGroupsAsync(
        bool forceRefresh = false, CancellationToken cancellationToken = default) =>
        Task.FromResult(ServiceResult<IReadOnlyList<StudentGroupOption>>.Success(_groups));

    public Task<ServiceResult<IReadOnlyList<SchoolOption>>> GetSchoolOptionsAsync(
        bool forceRefresh = false, CancellationToken cancellationToken = default) =>
        Task.FromResult(ServiceResult<IReadOnlyList<SchoolOption>>.Success(_schools));

    public Task<ServiceResult<IReadOnlyList<DishOption>>> GetDishOptionsAsync(
        int studentGroupId, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<DishOption> dishes = studentGroupId == 1
            ? [
                new DishOption { Id = 11, Code = "BM-A1", Name = "Bánh mì" },
                new DishOption { Id = 12, Code = "BM-A2", Name = "Bánh mì" }
              ]
            : [
                new DishOption { Id = 21, Code = "BM-B1", Name = "Bánh mì" },
                new DishOption { Id = 22, Code = "BM-B2", Name = "Bánh mì" }
              ];
        return Task.FromResult(ServiceResult<IReadOnlyList<DishOption>>.Success(dishes));
    }

    public Task<MenuSaveResult> SaveMenuAsync(
        ResolvedMenuRow row, CancellationToken cancellationToken = default) =>
        Task.FromResult(new MenuSaveResult(SaveBatchOutcome.Success, "OK", 200, 1));

    public void ClearCache() { }
}

file sealed class FakeBatchApiService(bool noProcessSteps) : IBatchApiService
{
    public Task<ServiceResult<FormOptionsData>> GetFormOptionsAsync(
        bool forceRefresh = false, CancellationToken cancellationToken = default) =>
        Task.FromResult(ServiceResult<FormOptionsData>.Success(new FormOptionsData
        {
            Foods = [new FoodOption { Id = 1, Code = "TP01", Name = "Thực phẩm Một" }],
            Warehouses = [new WarehouseOption { Id = 11, Code = "KHO01", Name = "Kho Một" }],
            SubSuppliers = [new SubSupplierOption { Id = 21, Code = "NCC01", Name = "Nhà cung cấp Một" }],
            Employees = [new EmployeeOption { Id = 31, Code = "NV01", Name = "Nhân viên Một" }],
            Facilities = [new FacilityOption
            {
                Id = 47, Code = "CS01", Name = "Cơ sở Một", Address = "Hà Nội"
            }]
        }));

    public Task<ServiceResult<FoodOptionsData>> GetFoodOptionsAsync(
        int supplierFoodId, CancellationToken cancellationToken = default) =>
        Task.FromResult(ServiceResult<FoodOptionsData>.Success(new FoodOptionsData
        {
            DefaultProcessId = 51,
            DefaultProcess = new ProcessOption { Id = 51, Code = "QT01", Name = "Quy trình Một" }
        }));

    public Task<ServiceResult<ProcessDetailData>> GetProcessAsync(
        int processId, CancellationToken cancellationToken = default) =>
        Task.FromResult(ServiceResult<ProcessDetailData>.Success(new ProcessDetailData
        {
            Id = 51,
            Name = "Quy trình Một",
            Steps = noProcessSteps
                ? []
                :
                [
                    new ProcessStepOption { SupplierStepId = 61, Code = "B1", Name = "Bước Một", Order = 1 },
                    new ProcessStepOption { SupplierStepId = 62, Code = "B2", Name = "Bước Hai", Order = 2 }
                ]
        }));

    public Task<SaveBatchResult> SaveBatchAsync(
        ResolvedBatchRow row, CancellationToken cancellationToken = default) =>
        Task.FromResult(new SaveBatchResult(SaveBatchOutcome.Success, "OK", 200, 1));

    public void ClearCache() { }
}
