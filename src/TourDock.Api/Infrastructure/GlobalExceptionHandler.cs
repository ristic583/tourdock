using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using TourDock.Application.Abstractions.Data;
using TourDock.Application.Abstractions.Payments;

namespace TourDock.Api.Infrastructure;

/// <summary>Turns unhandled exceptions into ProblemDetails without leaking internals.</summary>
internal sealed partial class GlobalExceptionHandler(IProblemDetailsService problemDetails, ILogger<GlobalExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (status, title, detail) = exception switch
        {
            // Another request changed the same data first (e.g. two bookings for the last seats).
            ConcurrencyConflictException or DbUpdateConcurrencyException => (StatusCodes.Status409Conflict, "Conflict",
                "The resource was changed by another request. Please reload and try again."),
            // Database constraints are the last line of defense when two requests race past the application checks.
            DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.ExclusionViolation } } =>
                (StatusCodes.Status409Conflict, "Conflict", "This overlaps an existing departure or season."),
            DuplicateKeyException
                or DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } } =>
                (StatusCodes.Status409Conflict, "Conflict", "This already exists."),
            // A departure stayed locked longer than lock_timeout (extreme contention): ask the client to retry.
            PostgresException { SqlState: PostgresErrorCodes.LockNotAvailable } or
                DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.LockNotAvailable } } =>
                (StatusCodes.Status409Conflict, "Conflict", "This departure is busy right now. Please try again."),
            PaymentProviderUnavailableException => (StatusCodes.Status503ServiceUnavailable, "Payment provider unavailable",
                "Payments are temporarily unavailable. Your booking is still held; please try again in a moment."),
            BadHttpRequestException { StatusCode: StatusCodes.Status413PayloadTooLarge } =>
                (StatusCodes.Status413PayloadTooLarge, "Payload too large", "The upload is larger than the allowed maximum."),
            InvalidDataException => (StatusCodes.Status413PayloadTooLarge, "Payload too large",
                "The upload is larger than the allowed maximum."),
            BadHttpRequestException bad => (bad.StatusCode, "Bad request", "The request could not be read."),
            _ => (StatusCodes.Status500InternalServerError, "Server error", "An unexpected error occurred."),
        };

        if (status == StatusCodes.Status503ServiceUnavailable)
        {
            httpContext.Response.Headers.RetryAfter = "5";
        }

        if (status >= 500)
        {
            LogUnhandled(logger, exception);
        }

        httpContext.Response.StatusCode = status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = { Status = status, Title = title, Detail = detail },
        });
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception")]
    private static partial void LogUnhandled(ILogger logger, Exception exception);
}
