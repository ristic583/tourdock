using System.Diagnostics;
using FluentValidation;
using Microsoft.Extensions.Logging;
using TourDock.Application.Abstractions.Messaging;
using TourDock.Domain.Common;

namespace TourDock.Application.Behaviors;

// Decorator order (outermost first): Logging -> Agency access -> Validation -> Handler.
// Logging is outermost so every request is traced, including refusals and validation failures, with its full duration.
// Agency access comes before validation, so an outsider gets 404 whatever they send (AgencyAccessDecorators.cs).
// Validation runs before the handler so invalid input never touches the database.
// Each handler saves through a single SaveChanges call, which EF Core wraps in a transaction.

internal sealed class LoggingCommandDecorator<TCommand, TResponse>(
    ICommandHandler<TCommand, TResponse> inner, ILogger<TCommand> logger) : ICommandHandler<TCommand, TResponse>
    where TCommand : ICommand<TResponse>
{
    public async Task<Result<TResponse>> Handle(TCommand command, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        var result = await inner.Handle(command, cancellationToken);
        Log.Completed(logger, typeof(TCommand).Name, result.IsSuccess ? "succeeded" : result.Error!.Code, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        return result;
    }
}

internal sealed class LoggingQueryDecorator<TQuery, TResponse>(
    IQueryHandler<TQuery, TResponse> inner, ILogger<TQuery> logger) : IQueryHandler<TQuery, TResponse>
    where TQuery : IQuery<TResponse>
{
    public async Task<Result<TResponse>> Handle(TQuery query, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        var result = await inner.Handle(query, cancellationToken);
        Log.Completed(logger, typeof(TQuery).Name, result.IsSuccess ? "succeeded" : result.Error!.Code, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        return result;
    }
}

internal sealed class ValidationCommandDecorator<TCommand, TResponse>(
    ICommandHandler<TCommand, TResponse> inner, IEnumerable<IValidator<TCommand>> validators) : ICommandHandler<TCommand, TResponse>
    where TCommand : ICommand<TResponse>
{
    public async Task<Result<TResponse>> Handle(TCommand command, CancellationToken cancellationToken)
    {
        var error = await Validation.ValidateAsync(command, validators, cancellationToken);
        return error is null ? await inner.Handle(command, cancellationToken) : Result<TResponse>.Failure(error);
    }
}

internal sealed class ValidationQueryDecorator<TQuery, TResponse>(
    IQueryHandler<TQuery, TResponse> inner, IEnumerable<IValidator<TQuery>> validators) : IQueryHandler<TQuery, TResponse>
    where TQuery : IQuery<TResponse>
{
    public async Task<Result<TResponse>> Handle(TQuery query, CancellationToken cancellationToken)
    {
        var error = await Validation.ValidateAsync(query, validators, cancellationToken);
        return error is null ? await inner.Handle(query, cancellationToken) : Result<TResponse>.Failure(error);
    }
}

internal static class Validation
{
    public static async Task<ValidationError?> ValidateAsync<T>(
        T request, IEnumerable<IValidator<T>> validators, CancellationToken cancellationToken)
    {
        var failures = ControlCharacters.FindIn(request)
            .Select(name => new FluentValidation.Results.ValidationFailure(name, ControlCharacters.Message))
            .ToList();
        foreach (var validator in validators)
        {
            var result = await validator.ValidateAsync(request, cancellationToken);
            failures.AddRange(result.Errors);
        }

        if (failures.Count == 0)
        {
            return null;
        }

        return new ValidationError(failures
            .GroupBy(f => ToCamelCase(f.PropertyName))
            .ToDictionary(g => g.Key, g => g.Select(f => f.ErrorMessage).Distinct().ToArray()));
    }

    private static string ToCamelCase(string name) =>
        string.IsNullOrEmpty(name) ? name : char.ToLowerInvariant(name[0]) + name[1..];
}

internal static partial class Log
{
    [LoggerMessage(Level = LogLevel.Information, Message = "{Request} {Outcome} in {ElapsedMs:0.0} ms")]
    public static partial void Completed(ILogger logger, string request, string outcome, double elapsedMs);
}

/// <summary>After a successful command that changes the public catalog, clears cached search results and tour pages.</summary>
internal sealed class CatalogInvalidationDecorator<TCommand, TResponse>(
    ICommandHandler<TCommand, TResponse> inner, TourDock.Application.Catalog.ICatalogCache cache) : ICommandHandler<TCommand, TResponse>
    where TCommand : ICommand<TResponse>
{
    public async Task<Result<TResponse>> Handle(TCommand command, CancellationToken cancellationToken)
    {
        var result = await inner.Handle(command, cancellationToken);
        if (result.IsSuccess && command is TourDock.Application.Catalog.IInvalidatesCatalog)
        {
            await cache.InvalidateAsync(cancellationToken);
        }

        return result;
    }
}
