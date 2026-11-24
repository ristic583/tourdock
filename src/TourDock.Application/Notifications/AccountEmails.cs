using Microsoft.Extensions.Logging;
using TourDock.Application.Abstractions;
using TourDock.Application.Abstractions.Authentication;
using TourDock.Application.Abstractions.Data;

namespace TourDock.Application.Notifications;

/// <summary>
/// Outbox message: email the user a link to confirm their address. Carries the user id only; the token is created when
/// the email is sent, so no usable token ever sits in the outbox table.
/// </summary>
public sealed record SendEmailConfirmationMessage(Guid NotificationId, Guid UserId) : IOutboxMessage;

/// <summary>Emails about the account itself rather than a booking.</summary>
public static class AccountEmails
{
    /// <summary>How long a confirmation link works. Asking for a new one replaces it.</summary>
    public static readonly TimeSpan ConfirmationLinkLifetime = TimeSpan.FromHours(48);

    public static EmailContent EmailConfirmation(UserContact user, string link)
    {
        const string Intro = "Thanks for signing up! Please confirm this is your email address, so agencies can reach you about your trips.";
        var note = $"The link works for {ConfirmationLinkLifetime.TotalHours:0} hours. If you didn't create a TourDock account, you can ignore this email.";

        var text = $"""
            Hi {user.DisplayName},

            {Intro}

            Confirm your email: {link}

            {note}

            TourDock
            """;

        var html = EmailLayout.Render(new EmailLayoutModel("Confirm your email to start booking tours.", $"Hi {user.DisplayName},", Intro)
        {
            Badge = ("Confirm your email", EmailTone.Neutral),
            Extra = "You can look around right away; booking a tour or listing your agency needs a confirmed address.",
            Button = ("Confirm my email", link),
            Note = note,
            Reason = "You're receiving this email because someone signed up for TourDock with this address.",
        });

        return new EmailContent(user.Email, user.DisplayName, "Confirm your email for TourDock", text, html);
    }

    public static string ConfirmationLink(string webBaseUrl, Guid userId, string token) =>
        $"{webBaseUrl.TrimEnd('/')}/verify-email?user={userId:D}&token={Uri.EscapeDataString(token)}";
}

/// <summary>
/// Sends the confirmation email. Recorded once delivered, so an outbox retry doesn't send a second link; a retry before
/// delivery creates a new token, which simply replaces the unsent one.
/// </summary>
internal sealed partial class SendEmailConfirmationHandler(
    IIdentityService identity,
    IUserDirectory users,
    IEmailSender sender,
    ISentNotificationLog sentLog,
    IUnitOfWork unitOfWork,
    NotificationSettings settings,
    ILogger<SendEmailConfirmationHandler> logger) : IOutboxMessageHandler<SendEmailConfirmationMessage>
{
    public async Task HandleAsync(SendEmailConfirmationMessage message, CancellationToken cancellationToken)
    {
        var key = $"{message.NotificationId:N}.confirm-email";
        if (await sentLog.WasSentAsync(key, cancellationToken))
        {
            return;
        }

        var contact = (await users.GetContactsAsync([message.UserId], cancellationToken)).GetValueOrDefault(message.UserId);
        if (contact is null || settings.WebBaseUrl is null)
        {
            LogSkipped(message.UserId, contact is null ? "user not found" : "no web base URL configured");
            return;
        }

        var token = await identity.CreateEmailConfirmationTokenAsync(message.UserId, cancellationToken);
        if (token is null)
        {
            return; // confirmed in the meantime
        }

        var content = AccountEmails.EmailConfirmation(contact, AccountEmails.ConfirmationLink(settings.WebBaseUrl, message.UserId, token));
        var delivery = await sender.SendAsync(new EmailMessage(key, content.ToAddress, content.ToName, content.Subject, content.Text, content.Html), cancellationToken);
        sentLog.Record(key, delivery);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        LogSent(message.UserId, delivery);
    }

    // Ids only: email addresses are personal data and stay out of the logs.
    [LoggerMessage(Level = LogLevel.Information, Message = "Email confirmation for user {UserId}: {Delivery}")]
    private partial void LogSent(Guid userId, EmailDelivery delivery);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Email confirmation for user {UserId} skipped: {Reason}")]
    private partial void LogSkipped(Guid userId, string reason);
}
