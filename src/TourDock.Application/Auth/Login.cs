using FluentValidation;
using TourDock.Application.Abstractions.Authentication;
using TourDock.Application.Abstractions.Data;
using TourDock.Application.Abstractions.Messaging;
using TourDock.Application.Notifications;
using TourDock.Domain.Common;

namespace TourDock.Application.Auth;

public sealed record LoginCommand(string Email, string Password) : ICommand<AuthResponse>;

internal sealed class LoginValidator : AbstractValidator<LoginCommand>
{
    public LoginValidator()
    {
        RuleFor(c => c.Email).NotEmpty().MaximumLength(254);
        RuleFor(c => c.Password).NotEmpty().MaximumLength(128);
    }
}

/// <summary>
/// Signs in with the right password and a confirmed email. With the right password but an unconfirmed email, a new
/// link is sent instead (the right password already proves it's the person who signed up, so this reveals nothing).
/// </summary>
internal sealed class LoginHandler(
    IIdentityService identity, ITokenService tokens, IEmailConfirmationGate emailGate, IOutbox outbox, IUnitOfWork unitOfWork,
    ILoginThrottle throttle)
    : ICommandHandler<LoginCommand, AuthResponse>
{
    public async Task<Result<AuthResponse>> Handle(LoginCommand command, CancellationToken cancellationToken)
    {
        if (throttle.IsThrottled(command.Email))
        {
            return AuthErrors.TooManyAttempts;
        }

        var user = await identity.CheckCredentialsAsync(command.Email, command.Password, cancellationToken);
        if (user.IsFailure)
        {
            if (user.Error == AuthErrors.InvalidCredentials)
            {
                throttle.RecordFailure(command.Email);
            }

            return user.Error;
        }

        throttle.Reset(command.Email);

        if ((await emailGate.EnsureConfirmedAsync(user.Value.Id, cancellationToken)).IsFailure)
        {
            outbox.Enqueue(new SendEmailConfirmationMessage(Guid.CreateVersion7(), user.Value.Id));
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return AuthErrors.SignInNeedsConfirmedEmail;
        }

        return new AuthResponse(user.Value.ToResponse(), await tokens.IssueAsync(user.Value, cancellationToken));
    }
}
