using System.Globalization;
using System.Text;

namespace HanoiCheck.Services.Menu;

public static class MenuValueMapper
{
    private static readonly IReadOnlyDictionary<string, (int Id, string Name)> Days =
        new Dictionary<string, (int, string)>(StringComparer.OrdinalIgnoreCase)
        {
            ["2"] = (1, "Thứ 2"), ["thu 2"] = (1, "Thứ 2"), ["thu hai"] = (1, "Thứ 2"),
            ["3"] = (2, "Thứ 3"), ["thu 3"] = (2, "Thứ 3"), ["thu ba"] = (2, "Thứ 3"),
            ["4"] = (3, "Thứ 4"), ["thu 4"] = (3, "Thứ 4"), ["thu tu"] = (3, "Thứ 4"),
            ["5"] = (4, "Thứ 5"), ["thu 5"] = (4, "Thứ 5"), ["thu nam"] = (4, "Thứ 5"),
            ["6"] = (5, "Thứ 6"), ["thu 6"] = (5, "Thứ 6"), ["thu sau"] = (5, "Thứ 6"),
            ["7"] = (6, "Thứ 7"), ["thu 7"] = (6, "Thứ 7"), ["thu bay"] = (6, "Thứ 7"),
            ["cn"] = (7, "Chủ nhật"), ["chu nhat"] = (7, "Chủ nhật"), ["chunhat"] = (7, "Chủ nhật")
        };

    private static readonly IReadOnlyDictionary<string, (int Id, string Name)> Meals =
        new Dictionary<string, (int, string)>(StringComparer.OrdinalIgnoreCase)
        {
            ["sang"] = (1, "Sáng"), ["bua sang"] = (1, "Sáng"),
            ["trua"] = (2, "Trưa"), ["bua trua"] = (2, "Trưa"),
            ["xe"] = (3, "Xế"), ["bua xe"] = (3, "Xế"),
            ["phu"] = (4, "Phụ"), ["bua phu"] = (4, "Phụ")
        };

    public static bool TryMapDay(string? value, out int id, out string name) =>
        TryMap(Days, value, out id, out name);

    public static bool TryMapMeal(string? value, out int id, out string name) =>
        TryMap(Meals, value, out id, out name);

    public static bool TryMapStatus(string? value, out bool status)
    {
        var normalized = Normalize(value);
        if (normalized is "dang ap dung" or "ap dung" or "active" or "true" or "1")
        {
            status = true;
            return true;
        }

        if (normalized is "ngung ap dung" or "khong ap dung" or "inactive" or "false" or "0")
        {
            status = false;
            return true;
        }

        status = false;
        return false;
    }

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

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

    private static bool TryMap(
        IReadOnlyDictionary<string, (int Id, string Name)> map,
        string? value,
        out int id,
        out string name)
    {
        if (map.TryGetValue(Normalize(value), out var result))
        {
            id = result.Id;
            name = result.Name;
            return true;
        }

        id = default;
        name = string.Empty;
        return false;
    }
}
