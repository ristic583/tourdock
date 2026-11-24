using TourDock.Domain.Common;

namespace TourDock.Api.Infrastructure;

/// <summary>Maps application results to HTTP responses. Failures become RFC 9457 ProblemDetails.</summary>
internal static class ResultExtensions
{
    public static IResult ToHttp<T>(this Result<T> result, Func<T, IResult> onSuccess) =>
        result.IsSuccess ? onSuccess(result.Value) : result.Error.ToProblem();

    public static IResult ToHttp<T>(this Result<T> result) => result.ToHttp(TypedResults.Ok);

    public static IResult ToProblem(this Error error)
    {
        var extensions = new Dictionary<string, object?>
        {
            ["code"] = error.Code,
            ["traceId"] = System.Diagnostics.Activity.Current?.TraceId.ToString()
        };

        if (error is ValidationError validation)
        {
            return TypedResults.ValidationProblem(
                validation.Errors, detail: error.Message, title: "Validation failed", extensions: extensions);
        }

        var (status, title) = error.Type switch
        {
            ErrorType.NotFound => (StatusCodes.Status404NotFound, "Not found"),
            ErrorType.Conflict => (StatusCodes.Status409Conflict, "Conflict"),
            ErrorType.Forbidden => (StatusCodes.Status403Forbidden, "Forbidden"),
            ErrorType.TooManyRequests => (StatusCodes.Status429TooManyRequests, "Too many requests"),
            ErrorType.Unavailable => (StatusCodes.Status503ServiceUnavailable, "Service unavailable"),
            _ => (StatusCodes.Status400BadRequest, "Bad request"),
        };

        return TypedResults.Problem(detail: error.Message, statusCode: status, title: title, extensions: extensions);
    }
}
