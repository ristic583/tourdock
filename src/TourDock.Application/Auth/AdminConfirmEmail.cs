using FluentValidation;
using TourDock.Application.Abstractions.Authentication;
using TourDock.Application.Abstractions.Data;
using TourDock.Application.Abstractions.Messaging;
using TourDock.Application.Common;
using TourDock.Domain.Common;

namespace TourDock.Application.Auth;

public sealed record AdminConfirmEmailCommand(string Email) : ICommand<Unit>;

internal sealed class AdminConfirmEmailValidator : AbstractValidator<AdminConfirmEmailCommand>
{
    public AdminConfirmEmailValidator() => RuleFor(c => c.Email).NotEmpty().EmailAddress().MaximumLength(254);
}

internal sealed class AdminConfirmEmailHandler(
    IIdentityService identity, AdminAudit audit, IUnitOfWork unitOfWork)
    : ICommandHandler<AdminConfirmEmailCommand, Unit>
{
    public async Task<Result<Unit>> Handle(AdminConfirmEmailCommand command, CancellationToken cancellationToken)
    {
        var confirmed = await identity.MarkEmailConfirmedAsync(command.Email.Trim(), cancellationToken);
        if (confirmed.IsFailure)
        {
            return confirmed.Error;
        }

        audit.Record(Reviews.AdminActions.UserConfirmEmail, "User", confirmed.Value, null);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}
