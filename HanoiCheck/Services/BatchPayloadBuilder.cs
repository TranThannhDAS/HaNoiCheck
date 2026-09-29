using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using HanoiCheck.Models;

namespace HanoiCheck.Services;

public sealed class BatchPayloadBuilder
{
    public MultipartFormDataContent BuildMultipartContent(ResolvedBatchRow row)
    {
        if (row.Status != ImportRowStatus.Valid || row.SupplierFoodId is null)
        {
            throw new InvalidOperationException("Chỉ có thể build payload cho dòng hợp lệ.");
        }

        var totalImageCount = row.Images.Count + row.BatchSteps.Sum(step => step.Images.Count);
        if (totalImageCount > 3)
        {
            throw new InvalidOperationException("Mỗi lô chỉ được đính kèm tối đa 3 file.");
        }

        var content = new MultipartFormDataContent();
        Add(content, "supplier_food_id", row.SupplierFoodId.Value.ToString(CultureInfo.InvariantCulture));
        Add(content, "code", row.Code);
        Add(content, "name", row.Name);
        Add(content, "import_date", row.ImportDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Add(content, "production_date", row.ProductionDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Add(content, "expire_date", row.ExpireDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Add(content, "purchase_address", row.PurchaseAddress);
        Add(content, "description", row.Description);

        for (var index = 0; index < row.SupplierWarehouseIds.Count; index++)
        {
            Add(content, $"supplier_warehouse_ids[{index}]", row.SupplierWarehouseIds[index].ToString());
        }

        for (var index = 0; index < row.SupplierSubSupplierIds.Count; index++)
        {
            Add(content, $"supplier_sub_supplier_ids[{index}]", row.SupplierSubSupplierIds[index].ToString());
        }

        for (var imageIndex = 0; imageIndex < row.Images.Count; imageIndex++)
        {
            AddImage(content, $"images[{imageIndex}]", row.Images[imageIndex]);
        }

        for (var stepIndex = 0; stepIndex < row.BatchSteps.Count; stepIndex++)
        {
            var step = row.BatchSteps[stepIndex];
            var prefix = $"batch_steps[{stepIndex}]";
            Add(content, $"{prefix}[supplier_step_id]", step.SupplierStepId.ToString());
            Add(content, $"{prefix}[supplier_facility_id]", step.SupplierFacilityId?.ToString());
            Add(content, $"{prefix}[address]", step.Address);
            Add(content, $"{prefix}[note]", step.Note);

            for (var employeeIndex = 0; employeeIndex < step.SupplierEmployeeIds.Count; employeeIndex++)
            {
                Add(content, $"{prefix}[supplier_employee_ids][{employeeIndex}]",
                    step.SupplierEmployeeIds[employeeIndex].ToString());
            }

            for (var imageIndex = 0; imageIndex < step.Images.Count; imageIndex++)
            {
                AddImage(content, $"{prefix}[images][{imageIndex}]", step.Images[imageIndex]);
            }
        }

        return content;
    }

    private static void AddImage(
        MultipartFormDataContent content,
        string prefix,
        ResolvedImage image)
    {
        var streamContent = new StreamContent(new FileStream(
            image.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read));
        streamContent.Headers.ContentType = new MediaTypeHeaderValue(GetContentType(image.FilePath));
        content.Add(streamContent, $"{prefix}[file]", Path.GetFileName(image.FilePath));
        Add(content, $"{prefix}[name]", image.Name);
    }

    private static void Add(MultipartFormDataContent content, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            content.Add(new StringContent(value), name);
        }
    }

    private static string GetContentType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        ".webp" => "image/webp",
        ".pdf" => "application/pdf",
        _ => "application/octet-stream"
    };
}
