namespace ShiftClub.Shared.Contracts;

public sealed class ApiResponse<T>
{
    public bool Success { get; init; }
    public T? Data { get; init; }
    public string? ErrorCode { get; init; }
    public string? Message { get; init; }
    public IReadOnlyList<string>? Errors { get; init; }

    public static ApiResponse<T> Ok(T data) => new()
    {
        Success = true,
        Data = data
    };

    public static ApiResponse<T> Fail(string errorCode, string message, IReadOnlyList<string>? errors = null) => new()
    {
        Success = false,
        ErrorCode = errorCode,
        Message = message,
        Errors = errors
    };
}

public sealed class ApiResponse
{
    public bool Success { get; init; }
    public string? ErrorCode { get; init; }
    public string? Message { get; init; }
    public IReadOnlyList<string>? Errors { get; init; }

    public static ApiResponse Ok(string? message = null) => new()
    {
        Success = true,
        Message = message
    };

    public static ApiResponse Fail(string errorCode, string message, IReadOnlyList<string>? errors = null) => new()
    {
        Success = false,
        ErrorCode = errorCode,
        Message = message,
        Errors = errors
    };
}
