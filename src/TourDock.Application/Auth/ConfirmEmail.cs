using FluentValidation;
using TourDock.Application.Abstractions.Authentication;
using TourDock.Application.Abstractions.Messaging;
using TourDock.Domain.Common;

namespace TourDock.Application.Auth;

public sealed record ConfirmEmailCommand(Guid UserId, string Token) : ICommand<Unit>;

internal sealed class ConfirmEmailValidator : AbstractValidator<ConfirmEmailCommand>
{
    public ConfirmEmailValidator()
    {
        RuleFor(c => c.UserId).NotEmpty();
        RuleFor(c => c.Token).NotEmpty().MaximumLength(200);
    }
}

/// <summary>Anonymous on purpose: the link may be opened on another device than the one signed in.</summary>
internal sealed class ConfirmEmailHandler(IIdentityService identity) : ICommandHandler<ConfirmEmailCommand, Unit>
{
    public Task<Result<Unit>> Handle(ConfirmEmailCommand command, CancellationToken cancellationToken) =>
        identity.ConfirmEmailAsync(command.UserId, command.Token, cancellationToken);
}
