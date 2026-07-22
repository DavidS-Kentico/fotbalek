using FluentValidation;
using Fotbalek.SharedKernel;
using MediatR;

namespace Fotbalek.Application.Common.Behaviors;

/// <summary>
/// Runs every registered FluentValidation validator for the request and short-circuits to a
/// failed <see cref="Result"/> (with field-level details for form display) before the handler
/// or the transaction ever start.
/// </summary>
public sealed class ValidationBehavior<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
    where TResponse : Result
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (!validators.Any())
            return await next(cancellationToken);

        var context = new ValidationContext<TRequest>(request);
        var failures = new List<FluentValidation.Results.ValidationFailure>();
        foreach (var validator in validators)
        {
            var result = await validator.ValidateAsync(context, cancellationToken);
            failures.AddRange(result.Errors);
        }

        if (failures.Count == 0)
            return await next(cancellationToken);

        var details = failures
            .GroupBy(f => f.PropertyName)
            .ToDictionary(g => g.Key, g => g.Select(f => f.ErrorMessage).ToArray());
        var error = Error.Validation(
            "Validation.Failed",
            failures[0].ErrorMessage,
            details);

        return CreateFailure(error);
    }

    /// <summary>Builds Result.Failure or Result&lt;T&gt;.Failure to match TResponse.</summary>
    private static TResponse CreateFailure(Error error)
    {
        if (typeof(TResponse) == typeof(Result))
            return (TResponse)Result.Failure(error);

        // TResponse is Result<T> — construct via the generic factory.
        var valueType = typeof(TResponse).GetGenericArguments()[0];
        var factory = typeof(Result)
            .GetMethods()
            .Single(m => m is { Name: nameof(Result.Failure), IsGenericMethodDefinition: true })
            .MakeGenericMethod(valueType);
        return (TResponse)factory.Invoke(null, [error])!;
    }
}
