using Microsoft.Win32;

namespace HanoiCheck.Services;

public interface IFilePickerService
{
    string? PickExcelFile();
}

public sealed class FilePickerService : IFilePickerService
{
    public string? PickExcelFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Chọn file Excel",
            Filter = "Excel Workbook (*.xlsx)|*.xlsx",
            CheckFileExists = true,
            Multiselect = false
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

}
