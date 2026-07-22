namespace Fotbalek.SharedKernel;

/// <summary>
/// Classifies an <see cref="Error"/> so the host can map it mechanically:
/// Blazor components render it inline, HTTP endpoints map it to a status code.
/// </summary>
public enum ErrorType
{
    Validation,
    NotFound,
    Conflict,
    Unauthorized,
    Forbidden,
    Failure,
}
