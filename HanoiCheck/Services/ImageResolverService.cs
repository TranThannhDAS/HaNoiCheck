using System.IO;
using HanoiCheck.Models;

namespace HanoiCheck.Services;

public sealed class ImageResolverService
{
    private const int MaximumFilesPerBatch = 3;
    private const long MaximumFileSize = 5 * 1024 * 1024;
    private static readonly HashSet<string> SupportedExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".webp", ".png", ".pdf" };

    public ServiceResult<IReadOnlyList<ResolvedImage>> ResolveFromExcel(
        string? source,
        string? excelDirectory)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return ServiceResult<IReadOnlyList<ResolvedImage>>.Success(Array.Empty<ResolvedImage>());
        }

        try
        {
            var sourceParts = source.Split([';', '|', '\n', '\r'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var files = new List<FileInfo>();
            foreach (var sourcePart in sourceParts)
            {
                var expandedPath = Environment.ExpandEnvironmentVariables(sourcePart.Trim().Trim('"'));
                var resolvedPath = Path.IsPathRooted(expandedPath)
                    ? expandedPath
                    : Path.GetFullPath(Path.Combine(excelDirectory ?? string.Empty, expandedPath));

                if (Directory.Exists(resolvedPath))
                {
                    files.AddRange(Directory.EnumerateFiles(resolvedPath)
                        .Where(path => SupportedExtensions.Contains(Path.GetExtension(path)))
                        .Select(path => new FileInfo(path)));
                }
                else if (File.Exists(resolvedPath) &&
                         SupportedExtensions.Contains(Path.GetExtension(resolvedPath)))
                {
                    files.Add(new FileInfo(resolvedPath));
                }
                else
                {
                    return ServiceResult<IReadOnlyList<ResolvedImage>>.Failure(
                        $"Không tìm thấy đường dẫn ảnh '{sourcePart}'.");
                }
            }

            files = files
                .GroupBy(file => file.FullName, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(file => file.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (files.Count == 0)
            {
                return ServiceResult<IReadOnlyList<ResolvedImage>>.Failure(
                    "Thư mục trong Excel không có file được hỗ trợ.");
            }

            if (files.Count > MaximumFilesPerBatch)
            {
                return ServiceResult<IReadOnlyList<ResolvedImage>>.Failure(
                    $"Thư mục chứa {files.Count} file; mỗi lô chỉ được đính kèm tối đa 3 file.");
            }

            var oversized = files.FirstOrDefault(file => file.Length > MaximumFileSize);
            if (oversized is not null)
            {
                return ServiceResult<IReadOnlyList<ResolvedImage>>.Failure(
                    $"File '{oversized.Name}' vượt quá giới hạn 5 MB.");
            }

            var resolved = files.Select(file => new ResolvedImage
                {
                    FilePath = file.FullName,
                    Name = Path.GetFileNameWithoutExtension(file.Name),
                    Size = file.Length
                })
                .ToList();

            return ServiceResult<IReadOnlyList<ResolvedImage>>.Success(resolved);
        }
        catch (Exception)
        {
            return ServiceResult<IReadOnlyList<ResolvedImage>>.Failure(
                "Không thể đọc thư mục ảnh đã chọn.");
        }
    }
}
