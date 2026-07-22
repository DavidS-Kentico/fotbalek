namespace Fotbalek.SharedKernel;

/// <summary>
/// An expected failure: a stable code (for programmatic checks and tests), a human-readable
/// message (safe to render), and a <see cref="ErrorType"/> for mechanical host mapping.
/// Validation errors optionally carry field-level details for form display.
/// </summary>
public sealed record Error
{
    private static readonly IReadOnlyDictionary<string, string[]> NoDetails =
        new Dictionary<string, string[]>();

    private Error(string code, string message, ErrorType type, IReadOnlyDictionary<string, string[]>? details = null)
    {
        Code = code;
        Message = message;
        Type = type;
        Details = details ?? NoDetails;
    }

    public string Code { get; }
    public string Message { get; }
    public ErrorType Type { get; }

    /// <summary>Field-level validation details (property name → messages). Empty except for validation errors.</summary>
    public IReadOnlyDictionary<string, string[]> Details { get; }

    public static Error Validation(string code, string message, IReadOnlyDictionary<string, string[]>? details = null) =>
        new(code, message, ErrorType.Validation, details);

    public static Error NotFound(string code, string message) => new(code, message, ErrorType.NotFound);

    public static Error Conflict(string code, string message) => new(code, message, ErrorType.Conflict);

    public static Error Unauthorized(string code, string message) => new(code, message, ErrorType.Unauthorized);

    public static Error Forbidden(string code, string message) => new(code, message, ErrorType.Forbidden);

    public static Error Failure(string code, string message) => new(code, message, ErrorType.Failure);
}
