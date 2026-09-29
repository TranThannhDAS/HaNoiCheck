namespace HanoiCheck.Models;

public sealed record ServiceResult<T>(bool IsSuccess, T? Value, string? ErrorMessage)
{
    public static ServiceResult<T> Success(T value) => new(true, value, null);

    public static ServiceResult<T> Failure(string message) => new(false, default, message);
}
