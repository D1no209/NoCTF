namespace NoCTF.Application.Common;

/// <summary>Represents a use-case outcome without exposing transport concerns.</summary>
public sealed record OperationResult(bool Succeeded, string? ErrorCode = null, string? ErrorMessage = null)
{
    public static OperationResult Success() => new(true);
    public static OperationResult Failure(string code, string message) => new(false, code, message);
}

/// <summary>Represents a use-case outcome carrying a value.</summary>
public sealed record OperationResult<T>(bool Succeeded, T? Value, string? ErrorCode = null, string? ErrorMessage = null)
{
    public static OperationResult<T> Success(T value) => new(true, value);
    public static OperationResult<T> Failure(string code, string message) => new(false, default, code, message);
}
