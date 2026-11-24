using TourDock.Application.Abstractions;
using TourDock.Application.Abstractions.Authentication;
using TourDock.Application.Abstractions.Data;
using TourDock.Application.Abstractions.Messaging;
using TourDock.Application.Notifications;
using TourDock.Domain.Common;

namespace TourDock.Application.Auth;

public sealed record ResendEmailConfirmationCommand : ICommand<Unit>;

/// <summary>Queues a fresh link for the signed-in user (the new token replaces the old one). A no-op once confirmed.</summary>
internal sealed class ResendEmailConfirmationHandler(IUserContext user, IIdentityService identity, IOutbox outbox, IUnitOfWork unitOfWork)
    : ICommandHandler<ResendEmailConfirmationCommand, Unit>
{
    public async Task<Result<Unit>> Handle(ResendEmailConfirmationCommand command, CancellationToken cancellationToken)
    {
        var found = await identity.FindByIdAsync(user.UserId, cancellationToken);
        if (found is null)
        {
            return AuthErrors.UserNotFound;
        }

        if (!found.EmailConfirmed)
        {
            outbox.Enqueue(new SendEmailConfirmationMessage(Guid.CreateVersion7(), found.Id));
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return Unit.Value;
    }
}
