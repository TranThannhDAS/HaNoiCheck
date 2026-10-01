using HanoiCheck.Models;

namespace HanoiCheck.Services.Menu;

public sealed class MenuResolverService(IMenuApiService menuApi)
{
    private readonly Dictionary<string, int> _sessionMappings = new(StringComparer.OrdinalIgnoreCase);

    public void ResetSessionMappings() => _sessionMappings.Clear();

    public void RememberSelection(MenuResolutionIssue issue, int selectedId)
    {
        _sessionMappings[MappingKey(issue.Kind, issue.SourceValue, issue.StudentGroupId)] = selectedId;
    }

    public async Task<ServiceResult<MenuValidationResult>> ResolveAsync(
        IReadOnlyList<MenuImportBlock> sourceRows,
        CancellationToken cancellationToken = default)
    {
        var groupTask = menuApi.GetStudentGroupsAsync(cancellationToken: cancellationToken);
        var schoolTask = menuApi.GetSchoolOptionsAsync(cancellationToken: cancellationToken);
        await Task.WhenAll(groupTask, schoolTask);

        var groups = await groupTask;
        var schools = await schoolTask;
        if (!groups.IsSuccess || groups.Value is null)
        {
            return ServiceResult<MenuValidationResult>.Failure(
                groups.ErrorMessage ?? "Không thể tải danh sách nhóm tuổi.");
        }

        if (!schools.IsSuccess || schools.Value is null)
        {
            return ServiceResult<MenuValidationResult>.Failure(
                schools.ErrorMessage ?? "Không thể tải danh sách trường.");
        }

        var result = new MenuValidationResult();
        foreach (var source in sourceRows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            result.Rows.Add(await ResolveRowAsync(
                source, groups.Value, schools.Value, cancellationToken));
        }

        return ServiceResult<MenuValidationResult>.Success(result);
    }

    private async Task<ResolvedMenuRow> ResolveRowAsync(
        MenuImportBlock source,
        IReadOnlyList<StudentGroupOption> groups,
        IReadOnlyList<SchoolOption> schools,
        CancellationToken cancellationToken)
    {
        var row = new ResolvedMenuRow { Source = source };
        row.Errors.AddRange(source.ParseErrors);
        row.Warnings.AddRange(source.ParseWarnings);

        ValidateText(row, source.Code, "Mã thực đơn");
        ValidateText(row, source.Name, "Tên thực đơn");

        if (MenuValueMapper.TryMapStatus(source.StatusText, out var status))
        {
            row.Status = status;
        }
        else
        {
            row.Errors.Add($"Trạng thái không hợp lệ: {Display(source.StatusText)}.");
        }

        if (MenuValueMapper.TryMapDay(source.DayValue, out var dayKey, out var dayName))
        {
            row.DayKey = dayKey;
            row.DayName = dayName;
        }
        else
        {
            row.Errors.Add($"Không xác định được thứ áp dụng: {Display(source.DayValue)}.");
        }

        ResolveStudentGroup(row, source.StudentGroupValue, groups);
        ResolveSchools(row, source.SchoolValues, schools);

        IReadOnlyList<DishOption>? dishes = null;
        if (row.StudentGroupId is int studentGroupId)
        {
            var dishResult = await menuApi.GetDishOptionsAsync(studentGroupId, cancellationToken);
            if (dishResult.IsSuccess && dishResult.Value is not null)
            {
                dishes = dishResult.Value;
            }
            else
            {
                row.Errors.Add(dishResult.ErrorMessage ?? "Không thể tải danh sách món ăn.");
            }
        }

        foreach (var sourceItem in source.Items)
        {
            var item = new ResolvedMenuItem
            {
                SourceRowNumber = sourceItem.RowNumber,
                SourceMealValue = sourceItem.MealValue,
                SourceDishValue = sourceItem.DishValue
            };

            if (MenuValueMapper.TryMapMeal(sourceItem.MealValue, out var mealId, out var mealName))
            {
                item.MealId = mealId;
                item.MealName = mealName;
            }
            else
            {
                row.Errors.Add($"Không xác định được bữa: {sourceItem.MealValue} (dòng {sourceItem.RowNumber}).");
            }

            if (dishes is not null && row.StudentGroupId is int scopedGroupId)
            {
                ResolveDish(row, item, sourceItem.DishValue, scopedGroupId, dishes);
            }

            row.Items.Add(item);
        }

        if (source.SchoolValues.Count == 0)
        {
            row.Errors.Add("Thực đơn chưa có trường áp dụng.");
        }

        if (source.Items.Count == 0)
        {
            row.Errors.Add("Thực đơn phải có ít nhất một món ăn.");
        }

        SetValidationStatus(row);
        return row;
    }

    private void ResolveStudentGroup(
        ResolvedMenuRow row,
        string? sourceValue,
        IReadOnlyList<StudentGroupOption> groups)
    {
        if (string.IsNullOrWhiteSpace(sourceValue))
        {
            row.Errors.Add("Nhóm tuổi không được để trống.");
            return;
        }

        var candidates = groups
            .Where(group => Same(group.Name, sourceValue) || Same(group.NutritionName, sourceValue))
            .DistinctBy(group => group.Id)
            .ToList();
        var selected = SelectCandidate(
            MenuResolutionKind.StudentGroup, sourceValue, null, candidates, group => group.Id);

        if (selected is not null)
        {
            row.StudentGroupId = selected.Id;
            row.StudentGroupName = selected.Name ?? selected.NutritionName;
        }
        else if (candidates.Count > 1)
        {
            row.ResolutionIssues.Add(new MenuResolutionIssue
            {
                Kind = MenuResolutionKind.StudentGroup,
                SourceValue = sourceValue,
                DisplayName = "nhóm tuổi",
                Candidates = candidates.Select(group => new MenuOptionCandidate
                {
                    Id = group.Id,
                    Name = group.Name ?? group.NutritionName,
                    Metadata = $"ID: {group.Id}"
                }).ToList()
            });
        }
        else
        {
            row.Errors.Add($"Không tìm thấy nhóm tuổi: {sourceValue}.");
        }
    }

    private void ResolveSchools(
        ResolvedMenuRow row,
        IReadOnlyList<string> sourceValues,
        IReadOnlyList<SchoolOption> schools)
    {
        foreach (var sourceValue in sourceValues)
        {
            var candidates = schools.Where(school => Same(school.Name, sourceValue))
                .DistinctBy(school => school.Id)
                .ToList();
            var selected = SelectCandidate(
                MenuResolutionKind.School, sourceValue, null, candidates, school => school.Id);
            if (selected is not null)
            {
                if (!row.SchoolIds.Contains(selected.Id))
                {
                    row.SchoolIds.Add(selected.Id);
                    row.Schools.Add(selected);
                }
            }
            else if (candidates.Count > 1)
            {
                row.ResolutionIssues.Add(new MenuResolutionIssue
                {
                    Kind = MenuResolutionKind.School,
                    SourceValue = sourceValue,
                    DisplayName = "trường áp dụng",
                    Candidates = candidates.Select(school => new MenuOptionCandidate
                    {
                        Id = school.Id,
                        Name = school.Name,
                        Metadata = $"ID: {school.Id}"
                    }).ToList()
                });
            }
            else
            {
                row.Errors.Add($"Không tìm thấy trường: {sourceValue}.");
            }
        }
    }

    private void ResolveDish(
        ResolvedMenuRow row,
        ResolvedMenuItem item,
        string sourceValue,
        int studentGroupId,
        IReadOnlyList<DishOption> dishes)
    {
        var codeMatches = dishes.Where(dish => Same(dish.Code, sourceValue))
            .DistinctBy(dish => dish.Id)
            .ToList();
        var candidates = codeMatches.Count > 0
            ? codeMatches
            : dishes.Where(dish => Same(dish.Name, sourceValue))
                .DistinctBy(dish => dish.Id)
                .ToList();
        var selected = SelectCandidate(
            MenuResolutionKind.Dish, sourceValue, studentGroupId, candidates, dish => dish.Id);

        if (selected is not null)
        {
            item.DishId = selected.Id;
            item.DishCode = selected.Code;
            item.DishName = selected.Name;
        }
        else if (candidates.Count > 1)
        {
            row.ResolutionIssues.Add(new MenuResolutionIssue
            {
                Kind = MenuResolutionKind.Dish,
                SourceValue = sourceValue,
                DisplayName = "món ăn",
                StudentGroupId = studentGroupId,
                Candidates = candidates.Select(dish => new MenuOptionCandidate
                {
                    Id = dish.Id,
                    Name = dish.Name,
                    Code = dish.Code,
                    Metadata = $"ID: {dish.Id}"
                }).ToList()
            });
        }
        else
        {
            row.Errors.Add($"Không tìm thấy món ăn: {sourceValue}.");
        }
    }

    private T? SelectCandidate<T>(
        MenuResolutionKind kind,
        string sourceValue,
        int? studentGroupId,
        IReadOnlyList<T> candidates,
        Func<T, int> idSelector)
        where T : class
    {
        if (_sessionMappings.TryGetValue(
                MappingKey(kind, sourceValue, studentGroupId), out var selectedId))
        {
            var remembered = candidates.FirstOrDefault(item => idSelector(item) == selectedId);
            if (remembered is not null)
            {
                return remembered;
            }
        }

        return candidates.Count == 1 ? candidates[0] : null;
    }

    private static void SetValidationStatus(ResolvedMenuRow row)
    {
        if (row.Errors.Count > 0)
        {
            row.ValidationStatus = ImportRowStatus.Error;
        }
        else if (row.ResolutionIssues.Count > 0)
        {
            row.ValidationStatus = ImportRowStatus.RequiresUserSelection;
        }
        else if (row.Status is null || row.StudentGroupId is null || row.DayKey is null ||
                 row.SchoolIds.Count == 0 || row.Items.Count == 0 ||
                 row.Items.Any(item => item.MealId is null || item.DishId is null))
        {
            row.ValidationStatus = ImportRowStatus.Error;
            row.Errors.Add("Thực đơn chưa đủ dữ liệu bắt buộc để lưu.");
        }
        else
        {
            row.ValidationStatus = ImportRowStatus.Valid;
        }
    }

    private static void ValidateText(ResolvedMenuRow row, string? value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            row.Errors.Add($"{fieldName} không được để trống.");
        }
    }

    private static string MappingKey(MenuResolutionKind kind, string value, int? groupId) =>
        $"{kind}:{groupId?.ToString() ?? "-"}:{value.Trim()}";

    private static bool Same(string? left, string? right) =>
        !string.IsNullOrWhiteSpace(left) && !string.IsNullOrWhiteSpace(right) &&
        string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);

    private static string Display(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "(trống)" : value;
}
