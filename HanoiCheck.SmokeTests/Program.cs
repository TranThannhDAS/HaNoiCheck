using HanoiCheck.Models;
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

Console.WriteLine("PASS: template blocks, repeated meal, semicolon schools, orphan item, dish ambiguity and scoped mapping.");

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
