namespace NoCTF.Application.Common;

public sealed record OperationResult<TFailure>(
    bool Succeeded,
    TFailure? FailureCode = null,
    string? ErrorMessage = null)
    where TFailure : struct, Enum
{
    public static OperationResult<TFailure> Success() => new(true);

    public static OperationResult<TFailure> Failure(TFailure code, string message) =>
        new(false, code, message);
}

public sealed record OperationResult<TValue, TFailure>(
    bool Succeeded,
    TValue? Value,
    TFailure? FailureCode = null,
    string? ErrorMessage = null)
    where TFailure : struct, Enum
{
    public static OperationResult<TValue, TFailure> Success(TValue value) => new(true, value);

    public static OperationResult<TValue, TFailure> Failure(TFailure code, string message) =>
        new(false, default, code, message);
}
