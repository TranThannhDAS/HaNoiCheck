using System.Globalization;
using System.IO;
using System.Text;
using ClosedXML.Excel;
using HanoiCheck.Models;

namespace HanoiCheck.Services;

public sealed class ExcelImportService
{
    private static readonly IReadOnlyDictionary<string, string[]> HeaderAliases =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["food_name"] = ["ten thuc pham", "thuc pham"],
            ["food_code"] = ["ma thuc pham"],
            ["batch_code"] = ["ma lo"],
            ["batch_name"] = ["ten lo"],
            ["import_date"] = ["ngay nhap"],
            ["expire_date"] = ["han dung"],
            ["warehouse_code"] = ["ma kho"],
            ["warehouse_name"] = ["ten kho", "kho", "kho phan bo"],
            ["sub_supplier_code"] = ["ma nha cung cap", "ma nha cung ung"],
            ["sub_supplier_name"] = ["ten nha cung cap", "nha cung cap", "nha cung ung"],
            ["employee_code"] = ["ma nhan vien"],
            ["employee_name"] = ["ten nhan vien", "nhan vien", "nhan vien thuc hien"],
            ["note"] = ["ghi chu", "thong tin san xuat"],
            ["image_source"] = ["anh", "thu muc anh", "duong dan anh", "anh va giay to minh chung cua lo"]
        };

    private static readonly string[] RequiredHeaders =
        ["food_name", "batch_code", "batch_name", "import_date"];

    public Task<ExcelImportResult> ReadAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() => Read(filePath, cancellationToken), cancellationToken);
    }

    private static ExcelImportResult Read(string filePath, CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                return ExcelImportResult.Failure("Không tìm thấy file Excel đã chọn.");
            }

            using var fileStream = File.Open(
                filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var workbook = new XLWorkbook(fileStream);
            var worksheet = workbook.Worksheets.FirstOrDefault();
            var range = worksheet?.RangeUsed();
            if (worksheet is null || range is null)
            {
                return ExcelImportResult.Failure("File Excel không có dữ liệu.");
            }

            var headerMatch = FindHeaderRow(worksheet, range);
            if (headerMatch is null)
            {
                return ExcelImportResult.Failure(
                    "Không tìm thấy dòng tiêu đề hợp lệ trong 20 dòng đầu của file Excel.");
            }

            var (headerRow, headers) = headerMatch.Value;

            var missingHeaders = RequiredHeaders.Where(header => !headers.ContainsKey(header)).ToList();
            if (missingHeaders.Count > 0)
            {
                return ExcelImportResult.Failure(
                    $"File Excel thiếu cột bắt buộc: {string.Join(", ", missingHeaders.Select(ToDisplayHeader))}.");
            }

            var rows = new List<ImportBatchRow>();
            var firstDataRow = headerRow.RowNumber() + 1;
            var lastRow = range.LastRow().RowNumber();
            for (var rowNumber = firstDataRow; rowNumber <= lastRow; rowNumber++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var row = worksheet.Row(rowNumber);
                if (row.IsEmpty())
                {
                    continue;
                }

                var foodName = Text(row, headers, "food_name");
                var batchCode = Text(row, headers, "batch_code");
                var batchName = Text(row, headers, "batch_name");
                if (string.IsNullOrWhiteSpace(foodName) &&
                    string.IsNullOrWhiteSpace(batchCode) &&
                    string.IsNullOrWhiteSpace(batchName))
                {
                    continue;
                }

                var employeeValue = Text(row, headers, "employee_name");
                var imageSource = Text(row, headers, "image_source");
                rows.Add(new ImportBatchRow
                {
                    SourceFileName = Path.GetFileName(filePath),
                    SourceDirectory = Path.GetDirectoryName(filePath),
                    RowNumber = rowNumber,
                    FoodCode = Text(row, headers, "food_code"),
                    FoodName = foodName,
                    Code = batchCode,
                    Name = batchName,
                    ImportDate = Date(row, headers, "import_date"),
                    ExpireDate = Date(row, headers, "expire_date"),
                    WarehouseCode = Text(row, headers, "warehouse_code"),
                    WarehouseName = Text(row, headers, "warehouse_name"),
                    SubSupplierCode = Text(row, headers, "sub_supplier_code"),
                    SubSupplierName = Text(row, headers, "sub_supplier_name"),
                    EmployeeCode = Text(row, headers, "employee_code") ?? employeeValue,
                    EmployeeName = employeeValue,
                    Note = Text(row, headers, "note"),
                    ImageSource = imageSource
                });
            }

            return rows.Count == 0
                ? ExcelImportResult.Failure("File Excel không có dòng dữ liệu hợp lệ để đọc.")
                : ExcelImportResult.Success(rows);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (IOException)
        {
            return ExcelImportResult.Failure(
                "Không thể đọc file Excel. Hãy đóng file trong Excel và thử lại.");
        }
        catch (Exception ex)
        {
            return ExcelImportResult.Failure($"Không thể đọc file Excel: {ex.Message}");
        }
    }

    private static string? Text(IXLRow row, IReadOnlyDictionary<string, int> headers, string name)
    {
        if (!headers.TryGetValue(name, out var column))
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
        var lastCandidateRow = Math.Min(range.LastRow().RowNumber(),
            range.FirstRow().RowNumber() + 19);
        for (var rowNumber = range.FirstRow().RowNumber(); rowNumber <= lastCandidateRow; rowNumber++)
        {
            var row = worksheet.Row(rowNumber);
            var headers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var cell in row.CellsUsed())
            {
                var normalized = NormalizeHeader(cell.GetString());
                foreach (var alias in HeaderAliases)
                {
                    if (!headers.ContainsKey(alias.Key) && alias.Value.Contains(
                        normalized, StringComparer.OrdinalIgnoreCase))
                    {
                        headers[alias.Key] = cell.Address.ColumnNumber;
                        break;
                    }
                }
            }

            if (RequiredHeaders.All(headers.ContainsKey))
            {
                return (row, headers);
            }
        }

        return null;
    }

    private static DateTime? Date(IXLRow row, IReadOnlyDictionary<string, int> headers, string name)
    {
        if (!headers.TryGetValue(name, out var column))
        {
            return null;
        }

        var cell = row.Cell(column);
        if (cell.TryGetValue<DateTime>(out var date))
        {
            return date.Date;
        }

        if (cell.TryGetValue<double>(out var serial) && serial is > 0 and < 2958466)
        {
            return DateTime.FromOADate(serial).Date;
        }

        var text = cell.GetFormattedString().Trim();
        var formats = new[] { "d/M/yyyy", "dd/MM/yyyy", "d-M-yyyy", "dd-MM-yyyy", "yyyy-MM-dd" };
        return DateTime.TryParseExact(text, formats, CultureInfo.InvariantCulture,
            DateTimeStyles.None, out date)
            ? date.Date
            : null;
    }

    private static string NormalizeHeader(string value)
    {
        var decomposed = value.Trim().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(char.ToLowerInvariant(character));
            }
        }

        return string.Join(' ', builder.ToString().Replace('đ', 'd')
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static string ToDisplayHeader(string normalized) => normalized switch
    {
        "food_name" => "THỰC PHẨM",
        "batch_code" => "MÃ LÔ",
        "batch_name" => "TÊN LÔ",
        "import_date" => "NGÀY NHẬP",
        _ => normalized
    };
}
