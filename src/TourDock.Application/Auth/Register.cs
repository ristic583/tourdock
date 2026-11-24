using FluentValidation;
using TourDock.Application.Abstractions.Authentication;
using TourDock.Application.Abstractions.Data;
using TourDock.Application.Abstractions.Messaging;
using TourDock.Application.Notifications;
using TourDock.Domain.Common;

namespace TourDock.Application.Auth;

public sealed record RegisterCommand(string Email, string Password, string DisplayName) : ICommand<Unit>;

internal sealed class RegisterValidator : AbstractValidator<RegisterCommand>
{
    public const int MinPasswordLength = 10;

    public RegisterValidator(IPasswordScreen passwords)
    {
        RuleFor(c => c.Email).NotEmpty().EmailAddress().MaximumLength(254);
        PasswordRules.Apply(RuleFor(c => c.Password), passwords);
        RuleFor(c => c.DisplayName).NotEmpty().MaximumLength(80);
    }
}

/// <summary>
/// Email-first sign-up: the answer is always "check your inbox", and the inbox tells the truth. A new address gets a
/// confirmation link (the account can sign in once it's opened). An address that already has an account gets either
/// a fresh link (never confirmed) or a notice that someone tried to sign up with it; its password doesn't change. So
/// the sign-up form can't be used to find out who has an account. Emails go through the outbox, so a mail server
/// hiccup only delays them.
/// </summary>
internal sealed class RegisterHandler(IIdentityService identity, IOutbox outbox, IUnitOfWork unitOfWork)
    : ICommandHandler<RegisterCommand, Unit>
{
    public async Task<Result<Unit>> Handle(RegisterCommand command, CancellationToken cancellationToken)
    {
        var signUp = await identity.RegisterAsync(command.Email, command.Password, command.DisplayName, cancellationToken);
        if (signUp.IsFailure)
        {
            return signUp.Error;
        }

        outbox.Enqueue(signUp.Value.Outcome == SignUpOutcome.ExistingConfirmed
            ? (IOutboxMessage)new SendAccountNoticeMessage(Guid.CreateVersion7(), AccountNoticeKind.SignUpWithExistingEmail,
                signUp.Value.UserId)
            : new SendEmailConfirmationMessage(Guid.CreateVersion7(), signUp.Value.UserId));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}
