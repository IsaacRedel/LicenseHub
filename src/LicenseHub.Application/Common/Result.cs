namespace LicenseHub.Application.Common;

/// <summary>A failure that callers can show or map to an HTTP status. <see cref="Code"/> is stable.</summary>
public sealed record Error(string Code, string Message);

/// <summary>Outcome of a use case: either a value or an <see cref="Error"/>.</summary>
public sealed class Result<T>
{
    private readonly T? _value;

    private Result(T value)
    {
        _value = value;
        IsSuccess = true;
    }

    private Result(Error error)
    {
        Error = error;
    }

    public bool IsSuccess { get; }

    public Error? Error { get; }

    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException($"Result failed with '{Error!.Code}' and has no value.");

    public static Result<T> Success(T value) => new(value);

    public static Result<T> Failure(Error error) => new(error);

    public static implicit operator Result<T>(T value) => Success(value);

    public static implicit operator Result<T>(Error error) => Failure(error);
}
