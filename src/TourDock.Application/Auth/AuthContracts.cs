using FluentValidation;
using TourDock.Application.Abstractions.Authentication;
using TourDock.Application.Notifications;
using TourDock.Domain.Common;

namespace TourDock.Application.Auth;

public sealed record AuthResponse(UserResponse User, AuthTokens Tokens);

public sealed record UserResponse(Guid Id, string Email, string DisplayName, IReadOnlyList<string> Roles, bool EmailConfirmed);

/// <summary>The rules for every new password: length, then (only if that passes) the breach list.</summary>
internal static class PasswordRules
{
    public const string BreachedMessage =
        "This password has appeared in a data breach, so attackers try it first. Please choose a different one.";

    public static void Apply<T>(IRuleBuilderInitial<T, string> rule, IPasswordScreen passwords) =>
        rule.Cascade(CascadeMode.Stop)
            .NotEmpty()
            .MinimumLength(RegisterValidator.MinPasswordLength)
            .WithMessage($"Use at least {RegisterValidator.MinPasswordLength} characters.")
            .MaximumLength(128)
            .MustAsync(async (password, ct) => !await passwords.IsBreachedAsync(password, ct))
            .WithMessage(BreachedMessage)
            .WithErrorCode("Auth.BreachedPassword");
}

internal static class AuthMapping
{
    public static UserResponse ToResponse(this AuthenticatedUser user) => new(user.Id, user.Email, user.DisplayName, user.Roles,
        user.EmailConfirmed);
}
