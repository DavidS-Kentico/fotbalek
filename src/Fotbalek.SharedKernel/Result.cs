using System.Diagnostics.CodeAnalysis;

namespace Fotbalek.SharedKernel;

/// <summary>
/// Outcome of an operation that can fail in an *expected* way. Unexpected exceptions are bugs
/// and are not converted to results — they bubble to host error handling.
/// </summary>
public class Result
{
    protected Result(bool isSuccess, Error? error)
    {
        if (isSuccess && error is not null)
            throw new ArgumentException("A successful result cannot carry an error.", nameof(error));
        if (!isSuccess && error is null)
            throw new ArgumentException("A failed result must carry an error.", nameof(error));

        IsSuccess = isSuccess;
        Error = error;
    }

    [MemberNotNullWhen(false, nameof(Error))]
    public bool IsSuccess { get; }

    [MemberNotNullWhen(true, nameof(Error))]
    public bool IsFailure => !IsSuccess;

    /// <summary>Non-null exactly when <see cref="IsFailure"/>.</summary>
    public Error? Error { get; }

    public static Result Success() => new(true, null);

    public static Result Failure(Error error) => new(false, error);

    public static Result<TValue> Success<TValue>(TValue value) => new(value, true, null);

    public static Result<TValue> Failure<TValue>(Error error) => new(default, false, error);
}

/// <summary>An operation outcome carrying a value on success.</summary>
public class Result<TValue> : Result
{
    private readonly TValue? _value;

    protected internal Result(TValue? value, bool isSuccess, Error? error)
        : base(isSuccess, error)
    {
        _value = value;
    }

    /// <summary>The value; throws when accessed on a failed result.</summary>
    public TValue Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException($"Cannot read the value of a failed result ({Error!.Code}).");

    public static implicit operator Result<TValue>(TValue value) => Success(value);
}
