using Microsoft.Extensions.Logging;
using TourDock.Application.Abstractions;
using TourDock.Application.Abstractions.Authentication;
using TourDock.Application.Abstractions.Data;
using TourDock.Application.Notifications;
using TourDock.Domain.Common;

namespace TourDock.Application.Auth;

/// <summary>Outbox message: the link that confirms the pending new address. The token is created when sending.</summary>
public sealed record SendEmailChangeLinkMessage(Guid NotificationId, Guid UserId) : IOutboxMessage;

internal sealed partial class SendEmailChangeLinkHandler(
    IIdentityService identity, IUserDirectory users, IEmailSender sender, ISentNotificationLog sentLog, IUnitOfWork unitOfWork,
    NotificationSettings settings, ILogger<SendEmailChangeLinkHandler> logger) : IOutboxMessageHandler<SendEmailChangeLinkMessage>
{
    public async Task HandleAsync(SendEmailChangeLinkMessage message, CancellationToken cancellationToken)
    {
        var key = $"{message.NotificationId:N}.email-change";
        if (await sentLog.WasSentAsync(key, cancellationToken))
        {
            return;
        }

        var contact = (await users.GetContactsAsync([message.UserId], cancellationToken)).GetValueOrDefault(message.UserId);
        if (contact is null || settings.WebBaseUrl is null)
        {
            LogSkipped(message.UserId);
            return;
        }

        var created = await identity.CreateEmailChangeTokenAsync(message.UserId, cancellationToken);
        if (created is not { } change)
        {
            return; // completed or replaced in the meantime
        }

        var content = EmailChangeEmails.Link(contact.DisplayName, change.NewEmail, EmailChangeEmails.ChangeLink(settings.WebBaseUrl,
            message.UserId, change.Token));
        var delivery = await sender.SendAsync(new EmailMessage(key, content.ToAddress, content.ToName, content.Subject, content.Text,
            content.Html), cancellationToken);
        sentLog.Record(key, delivery);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        LogSent(message.UserId, delivery);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Email change link for user {UserId}: {Delivery}")]
    private partial void LogSent(Guid userId, EmailDelivery delivery);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Email change link for user {UserId} skipped: user not found or no web base URL")]
    private partial void LogSkipped(Guid userId);
}
