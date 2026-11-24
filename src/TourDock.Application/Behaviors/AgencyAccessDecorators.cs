using TourDock.Application.Abstractions.Messaging;
using TourDock.Application.Common;
using TourDock.Domain.Common;

namespace TourDock.Application.Behaviors;

/// <summary>Checks agency access for agency-scoped commands (managers only) before validation and the handler.</summary>
internal sealed class AgencyAccessCommandDecorator<TCommand, TResponse>(ICommandHandler<TCommand, TResponse> inner, IAgencyAccess access)
    : ICommandHandler<TCommand, TResponse>
    where TCommand : ICommand<TResponse>
{
    public async Task<Result<TResponse>> Handle(TCommand command, CancellationToken cancellationToken)
    {
        if (command is IAgencyRequest scoped
            && await access.RequireAsync(scoped.AgencyId, AgencyAccessLevel.Manage, cancellationToken) is { IsFailure: true } denied)
        {
            return Result<TResponse>.Failure(denied.Error);
        }

        return await inner.Handle(command, cancellationToken);
    }
}

/// <summary>Checks agency access for agency-scoped queries (members and admins) before validation and the handler.</summary>
internal sealed class AgencyAccessQueryDecorator<TQuery, TResponse>(IQueryHandler<TQuery, TResponse> inner, IAgencyAccess access)
    : IQueryHandler<TQuery, TResponse>
    where TQuery : IQuery<TResponse>
{
    public async Task<Result<TResponse>> Handle(TQuery query, CancellationToken cancellationToken)
    {
        if (query is IAgencyRequest scoped
            && await access.RequireAsync(scoped.AgencyId, AgencyAccessLevel.Read, cancellationToken) is { IsFailure: true } denied)
        {
            return Result<TResponse>.Failure(denied.Error);
        }

        return await inner.Handle(query, cancellationToken);
    }
}
