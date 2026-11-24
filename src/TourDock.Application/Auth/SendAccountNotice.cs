using Microsoft.Extensions.Logging;
using TourDock.Application.Abstractions;
using TourDock.Application.Abstractions.Authentication;
using TourDock.Application.Abstractions.Data;
using TourDock.Application.Notifications;
using TourDock.Domain.Common;

namespace TourDock.Application.Auth;

/// <summary>Outbox message: a security notice about the account. Ids only; addresses are read when sending.</summary>
public sealed record SendAccountNoticeMessage(Guid NotificationId, AccountNoticeKind Kind, Guid UserId) : IOutboxMessage;

internal sealed partial class SendAccountNoticeHandler(
    IUserDirectory users, IIdentityService identity, IEmailSender sender, ISentNotificationLog sentLog, IUnitOfWork unitOfWork,
    NotificationSettings settings, ILogger<SendAccountNoticeHandler> logger) : IOutboxMessageHandler<SendAccountNoticeMessage>
{
    public async Task HandleAsync(SendAccountNoticeMessage message, CancellationToken cancellationToken)
    {
        var key = $"{message.NotificationId:N}.account-notice";
        if (await sentLog.WasSentAsync(key, cancellationToken))
        {
            return;
        }

        var contact = (await users.GetContactsAsync([message.UserId], cancellationToken)).GetValueOrDefault(message.UserId);
        if (contact is null)
        {
            LogSkipped(message.UserId);
            return;
        }

        // The pending address is only shown masked; it is read now (ids only in the outbox).
        var pending = message.Kind == AccountNoticeKind.EmailChangeRequested
            ? await identity.GetPendingEmailAsync(message.UserId, cancellationToken)
            : null;
        var link = settings.WebBaseUrl is null ? null : $"{settings.WebBaseUrl.TrimEnd('/')}/account";
        var content = EmailChangeEmails.Notice(message.Kind, contact, pending, link);
        var delivery = await sender.SendAsync(new EmailMessage(key, content.ToAddress, content.ToName, content.Subject, content.Text,
            content.Html), cancellationToken);
        sentLog.Record(key, delivery);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        LogSent(message.Kind, message.UserId, delivery);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Account notice {Kind} for user {UserId}: {Delivery}")]
    private partial void LogSent(AccountNoticeKind kind, Guid userId, EmailDelivery delivery);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Account notice for user {UserId} skipped: user not found")]
    private partial void LogSkipped(Guid userId);
}
