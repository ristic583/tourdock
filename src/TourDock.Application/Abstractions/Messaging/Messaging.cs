using TourDock.Domain.Common;

namespace TourDock.Application.Abstractions.Messaging;

/// <summary>A use case that changes state.</summary>
public interface ICommand<TResponse>;

/// <summary>A use case that only reads state.</summary>
public interface IQuery<TResponse>;

public interface ICommandHandler<in TCommand, TResponse>
    where TCommand : ICommand<TResponse>
{
    Task<Result<TResponse>> Handle(TCommand command, CancellationToken cancellationToken);
}

public interface IQueryHandler<in TQuery, TResponse>
    where TQuery : IQuery<TResponse>
{
    Task<Result<TResponse>> Handle(TQuery query, CancellationToken cancellationToken);
}

/// <summary>Response of a command that returns nothing.</summary>
public readonly record struct Unit
{
    public static readonly Unit Value;
}
