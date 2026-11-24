using FluentValidation;
using TourDock.Application.Abstractions.Authentication;
using TourDock.Application.Abstractions.Messaging;
using TourDock.Domain.Common;

namespace TourDock.Application.Auth;

public sealed record ConfirmEmailChangeCommand(Guid UserId, string Token) : ICommand<Unit>;

internal sealed class ConfirmEmailChangeValidator : AbstractValidator<ConfirmEmailChangeCommand>
{
    public ConfirmEmailChangeValidator()
    {
        RuleFor(c => c.UserId).NotEmpty();
        RuleFor(c => c.Token).NotEmpty().MaximumLength(200);
    }
}

internal sealed class ConfirmEmailChangeHandler(IIdentityService identity) : ICommandHandler<ConfirmEmailChangeCommand, Unit>
{
    public Task<Result<Unit>> Handle(ConfirmEmailChangeCommand command, CancellationToken cancellationToken) =>
        identity.ConfirmEmailChangeAsync(command.UserId, command.Token, cancellationToken);
}
