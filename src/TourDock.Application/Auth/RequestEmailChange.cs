using FluentValidation;
using TourDock.Application.Abstractions;
using TourDock.Application.Abstractions.Authentication;
using TourDock.Application.Abstractions.Data;
using TourDock.Application.Abstractions.Messaging;
using TourDock.Domain.Common;

namespace TourDock.Application.Auth;

public sealed record RequestEmailChangeCommand(string NewEmail, string Password) : ICommand<Unit>;

internal sealed class RequestEmailChangeValidator : AbstractValidator<RequestEmailChangeCommand>
{
    public RequestEmailChangeValidator()
    {
        RuleFor(c => c.NewEmail).NotEmpty().EmailAddress().MaximumLength(254);
        RuleFor(c => c.Password).NotEmpty().MaximumLength(128);
    }
}

/// <summary>
/// Checks the password, then emails a link to the new address and a heads-up to the current one. The address only
/// changes when the link is opened. If the new address already has an account, its owner is told instead, and the
/// answer is the same as on success, so nobody can probe which addresses are registered.
/// </summary>
internal sealed class RequestEmailChangeHandler(IUserContext user, IIdentityService identity, IOutbox outbox, IUnitOfWork unitOfWork)
    : ICommandHandler<RequestEmailChangeCommand, Unit>
{
    public async Task<Result<Unit>> Handle(RequestEmailChangeCommand command, CancellationToken cancellationToken)
    {
        var request = await identity.RequestEmailChangeAsync(user.UserId, command.Password, command.NewEmail, cancellationToken);
        if (request.IsFailure)
        {
            return request.Error;
        }

        if (request.Value.ExistingOwnerId is { } owner)
        {
            outbox.Enqueue(new SendAccountNoticeMessage(Guid.CreateVersion7(), AccountNoticeKind.EmailAlreadyRegistered, owner));
        }
        else
        {
            outbox.Enqueue(new SendAccountNoticeMessage(Guid.CreateVersion7(), AccountNoticeKind.EmailChangeRequested, user.UserId));
            outbox.Enqueue(new SendEmailChangeLinkMessage(Guid.CreateVersion7(), user.UserId));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}
