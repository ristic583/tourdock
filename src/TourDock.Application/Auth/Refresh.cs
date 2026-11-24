using FluentValidation;
using TourDock.Application.Abstractions.Authentication;
using TourDock.Application.Abstractions.Messaging;
using TourDock.Domain.Common;

namespace TourDock.Application.Auth;

public sealed record RefreshCommand(string RefreshToken) : ICommand<AuthTokens>;

internal sealed class RefreshValidator : AbstractValidator<RefreshCommand>
{
    public RefreshValidator() => RuleFor(c => c.RefreshToken).NotEmpty().MaximumLength(200);
}

internal sealed class RefreshHandler(ITokenService tokens) : ICommandHandler<RefreshCommand, AuthTokens>
{
    public Task<Result<AuthTokens>> Handle(RefreshCommand command, CancellationToken cancellationToken) =>
        tokens.RefreshAsync(command.RefreshToken, cancellationToken);
}
