using ClosedXML.Excel;
using HanoiCheck.Models;
using System.IO;

namespace HanoiCheck.Services.Menu;

public sealed class MenuExcelImportService
{
    private static readonly IReadOnlyDictionary<string, string[]> HeaderAliases =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["code"] = ["ma thuc don", "ma menu"],
            ["name"] = ["ten thuc don", "ten menu"],
            ["status"] = ["trang thai"],
            ["student_group"] = ["nhom tuoi", "nhom dinh duong", "student group"],
            ["day"] = ["thu ap dung", "ngay ap dung", "thu"],
            ["schools"] = ["truong ap dung", "truong", "danh sach truong"],
            ["meal"] = ["bua", "bua an"],
            ["dish"] = ["mon an", "mon", "danh sach mon an"]
        };

    private static readonly string[] RequiredHeaders =
        ["code", "name", "status", "student_group", "day", "schools", "meal", "dish"];

    public Task<MenuExcelImportResult> ReadAsync(
        string filePath,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => Read(filePath, cancellationToken), cancellationToken);

    private static MenuExcelImportResult Read(string filePath, CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                return MenuExcelImportResult.Failure("Không tìm thấy file Excel đã chọn.");
            }

            using var stream = File.Open(
                filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var workbook = new XLWorkbook(stream);
            var worksheet = workbook.Worksheets.FirstOrDefault();
            var range = worksheet?.RangeUsed();
            if (worksheet is null || range is null)
            {
                return MenuExcelImportResult.Failure("File Excel không có dữ liệu.");
            }

            var headerMatch = FindHeaderRow(worksheet, range);
            if (headerMatch is null)
            {
                return MenuExcelImportResult.Failure(
                    "Không tìm thấy dòng tiêu đề thực đơn hợp lệ trong 20 dòng đầu của file Excel.");
            }

            var (headerRow, headers) = headerMatch.Value;
            var missing = RequiredHeaders.Where(key => !headers.ContainsKey(key)).ToList();
            if (missing.Count > 0)
            {
                return MenuExcelImportResult.Failure(
                    $"File Excel thiếu cột bắt buộc: {string.Join(", ", missing.Select(ToDisplayHeader))}.");
            }

            var dataRows = new List<MenuExcelDataRow>();
            for (var rowNumber = headerRow.RowNumber() + 1;
                 rowNumber <= range.LastRow().RowNumber();
                 rowNumber++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var excelRow = worksheet.Row(rowNumber);
                if (excelRow.IsEmpty())
                {
                    continue;
                }

                var code = Text(excelRow, headers, "code");
                var name = Text(excelRow, headers, "name");
                var meal = Text(excelRow, headers, "meal");
                var dish = Text(excelRow, headers, "dish");
                if (string.IsNullOrWhiteSpace(code) && string.IsNullOrWhiteSpace(name) &&
                    string.IsNullOrWhiteSpace(meal) && string.IsNullOrWhiteSpace(dish))
                {
                    continue;
                }

                dataRows.Add(new MenuExcelDataRow(
                    rowNumber,
                    code,
                    name,
                    Text(excelRow, headers, "status"),
                    Text(excelRow, headers, "student_group"),
                    Text(excelRow, headers, "day"),
                    Text(excelRow, headers, "schools"),
                    meal,
                    dish));
            }

            var rows = ParseRows(dataRows, Path.GetFileName(filePath));

            return rows.Count == 0
                ? MenuExcelImportResult.Failure("File Excel không có thực đơn nào để đọc.")
                : MenuExcelImportResult.Success(rows);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (IOException)
        {
            return MenuExcelImportResult.Failure(
                "Không thể đọc file Excel. Hãy đóng file trong Excel và thử lại.");
        }
        catch (Exception ex)
        {
            return MenuExcelImportResult.Failure($"Không thể đọc file Excel: {ex.Message}");
        }
    }

    public static IReadOnlyList<MenuImportBlock> ParseRows(
        IEnumerable<MenuExcelDataRow> dataRows,
        string? sourceFileName = null)
    {
        var blocks = new List<MenuImportBlock>();
        MenuImportBlock? current = null;
        foreach (var dataRow in dataRows.OrderBy(row => row.RowNumber))
        {
            if (!string.IsNullOrWhiteSpace(dataRow.Code))
            {
                if (current is not null)
                {
                    blocks.Add(current);
                }

                current = NewBlock(sourceFileName, dataRow.RowNumber, dataRow.Code);
                current.Name = dataRow.Name;
                current.StatusText = dataRow.Status;
                current.StudentGroupValue = dataRow.StudentGroup;
                current.DayValue = dataRow.Day;
                current.SchoolValues.AddRange(SplitSchools(dataRow.Schools));
            }

            if (string.IsNullOrWhiteSpace(dataRow.Meal) && string.IsNullOrWhiteSpace(dataRow.Dish))
            {
                continue;
            }

            if (current is null)
            {
                current = NewBlock(sourceFileName, dataRow.RowNumber, null);
                current.ParseErrors.Add(
                    $"Dòng {dataRow.RowNumber} có món ăn nhưng chưa thuộc thực đơn nào.");
            }

            AddItem(current, dataRow.Meal, dataRow.Dish, dataRow.RowNumber);
        }

        if (current is not null)
        {
            blocks.Add(current);
        }

        return blocks;
    }

    private static MenuImportBlock NewBlock(string? sourceFileName, int rowNumber, string? code) => new()
    {
        SourceFileName = sourceFileName,
        RowNumber = rowNumber,
        Code = code
    };

    private static void AddItem(
        MenuImportBlock row,
        string? meal,
        string? dish,
        int rowNumber)
    {
        if (string.IsNullOrWhiteSpace(meal))
        {
            row.ParseErrors.Add($"Dòng {rowNumber} thiếu Bữa.");
        }

        if (string.IsNullOrWhiteSpace(dish))
        {
            row.ParseErrors.Add($"Dòng {rowNumber} thiếu Món ăn.");
        }

        row.Items.Add(new MenuImportItem
        {
            MealValue = meal?.Trim() ?? string.Empty,
            DishValue = dish?.Trim() ?? string.Empty,
            RowNumber = rowNumber
        });
    }

    private static List<string> SplitSchools(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .ToList();

    private static string? Text(IXLRow row, IReadOnlyDictionary<string, int> headers, string key)
    {
        if (!headers.TryGetValue(key, out var column))
        {
            return null;
        }

        var value = row.Cell(column).GetFormattedString().Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static (IXLRow Row, Dictionary<string, int> Headers)? FindHeaderRow(
        IXLWorksheet worksheet,
        IXLRange range)
    {
        var lastCandidate = Math.Min(range.LastRow().RowNumber(), range.FirstRow().RowNumber() + 19);
        for (var rowNumber = range.FirstRow().RowNumber(); rowNumber <= lastCandidate; rowNumber++)
        {
            var headers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var cell in worksheet.Row(rowNumber).CellsUsed())
            {
                var normalized = MenuValueMapper.Normalize(cell.GetString());
                foreach (var alias in HeaderAliases)
                {
                    if (!headers.ContainsKey(alias.Key) && alias.Value.Contains(normalized))
                    {
                        headers[alias.Key] = cell.Address.ColumnNumber;
                        break;
                    }
                }
            }

            if (headers.Count >= 4 && headers.ContainsKey("code") && headers.ContainsKey("name"))
            {
                return (worksheet.Row(rowNumber), headers);
            }
        }

        return null;
    }

    private static string ToDisplayHeader(string key) => key switch
    {
        "code" => "MÃ THỰC ĐƠN",
        "name" => "TÊN THỰC ĐƠN",
        "status" => "TRẠNG THÁI",
        "student_group" => "NHÓM TUỔI",
        "day" => "THỨ ÁP DỤNG",
        "schools" => "TRƯỜNG ÁP DỤNG",
        "meal" => "BỮA",
        "dish" => "MÓN ĂN",
        _ => key
    };
}

public sealed record MenuExcelDataRow(
    int RowNumber,
    string? Code,
    string? Name,
    string? Status,
    string? StudentGroup,
    string? Day,
    string? Schools,
    string? Meal,
    string? Dish);
