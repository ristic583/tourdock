using TourDock.Application.Abstractions;
using TourDock.Application.Abstractions.Authentication;
using TourDock.Application.Abstractions.Messaging;
using TourDock.Domain.Common;

namespace TourDock.Application.Auth;

public sealed record GetCurrentUserQuery : IQuery<UserResponse>;

internal sealed class GetCurrentUserHandler(IUserContext user, IIdentityService identity) : IQueryHandler<GetCurrentUserQuery, UserResponse>
{
    public async Task<Result<UserResponse>> Handle(GetCurrentUserQuery query, CancellationToken cancellationToken)
    {
        var found = await identity.FindByIdAsync(user.UserId, cancellationToken);
        return found is null ? AuthErrors.UserNotFound : found.ToResponse();
    }
}
