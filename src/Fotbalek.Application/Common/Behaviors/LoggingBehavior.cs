using System.Diagnostics;
using Fotbalek.SharedKernel;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Fotbalek.Application.Common.Behaviors;

/// <summary>First in the pipeline (Logging → Validation → Transaction): request name, duration,
/// and the error code on expected failures. Unexpected exceptions bubble to host error handling.</summary>
public sealed class LoggingBehavior<TRequest, TResponse>(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).Name;
        var stopwatch = Stopwatch.StartNew();

        var response = await next(cancellationToken);

        stopwatch.Stop();
        if (response is Result { IsFailure: true } failure)
        {
            logger.LogInformation(
                "{RequestName} failed in {ElapsedMs} ms: {ErrorCode} ({ErrorType})",
                requestName, stopwatch.ElapsedMilliseconds, failure.Error.Code, failure.Error.Type);
        }
        else
        {
            logger.LogDebug(
                "{RequestName} handled in {ElapsedMs} ms",
                requestName, stopwatch.ElapsedMilliseconds);
        }

        return response;
    }
}
