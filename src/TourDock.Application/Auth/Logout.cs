using TourDock.Application.Abstractions.Authentication;
using TourDock.Application.Abstractions.Messaging;
using TourDock.Domain.Common;

namespace TourDock.Application.Auth;

public sealed record LogoutCommand(string RefreshToken) : ICommand<Unit>;

internal sealed class LogoutHandler(ITokenService tokens) : ICommandHandler<LogoutCommand, Unit>
{
    public async Task<Result<Unit>> Handle(LogoutCommand command, CancellationToken cancellationToken)
    {
        await tokens.RevokeAsync(command.RefreshToken, cancellationToken);
        return Unit.Value;
    }
}
