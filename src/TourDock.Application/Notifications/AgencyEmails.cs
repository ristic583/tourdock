using Microsoft.Extensions.Logging;
using TourDock.Application.Abstractions;
using TourDock.Application.Abstractions.Data;

namespace TourDock.Application.Notifications;

public enum AgencyNotificationKind
{
    Approved,
    Suspended,
    Reactivated,
}

/// <summary>Outbox message: tell an agency that an admin changed its status. Ids only; the reason is read when sending.</summary>
public sealed record SendAgencyStatusMessage(Guid NotificationId, AgencyNotificationKind Kind, Guid AgencyId) : IOutboxMessage;

/// <summary>Emails to an agency's contact address about the agency itself.</summary>
public static class AgencyEmails
{
    public static EmailContent StatusChanged(AgencyNotificationKind kind, string agencyName, string contactEmail, string? reason, string? link)
    {
        var (subject, badge, intro, extra, button) = kind switch
        {
            AgencyNotificationKind.Approved => (
                $"{agencyName} is approved on TourDock",
                ("Approved", EmailTone.Positive),
                "Good news: your agency is approved. Travelers can now find and book the tours you publish.",
                "Next: add your vehicles, create a tour with photos, a schedule and a cancellation policy, then publish it.",
                "Open your agency"),
            AgencyNotificationKind.Suspended => (
                $"{agencyName} is suspended on TourDock",
                ("Suspended", EmailTone.Negative),
                "An admin suspended your agency. Your tours are hidden from travelers until the suspension is lifted. Existing bookings are not cancelled.",
                reason is null ? null : $"Reason: {reason}",
                "Open your agency"),
            _ => (
                $"{agencyName} is active again on TourDock",
                ("Active again", EmailTone.Positive),
                "The suspension of your agency was lifted. Your published tours are visible to travelers again.",
                (string?)null,
                "Open your agency"),
        };

        var text = $"""
            Hello {agencyName},

            {intro}
            {(extra is null ? string.Empty : $"\n{extra}\n")}
            {(link is null ? string.Empty : $"{button}: {link}\n")}
            TourDock
            """;

        var html = EmailLayout.Render(new EmailLayoutModel(Preheader: intro, Greeting: $"Hello {agencyName},", Intro: intro)
        {
            Badge = badge,
            Extra = extra,
            Button = link is null ? null : (button, link),
            Reason = "You're receiving this email because your agency is registered on TourDock.",
        });

        return new EmailContent(contactEmail, agencyName, subject, text, html);
    }
}

/// <summary>Sends the agency status email once (recorded after delivery, so an outbox retry doesn't send it twice).</summary>
internal sealed partial class SendAgencyStatusHandler(
    IAgencyRepository agencies,
    IEmailSender sender,
    ISentNotificationLog sentLog,
    IUnitOfWork unitOfWork,
    NotificationSettings settings,
    ILogger<SendAgencyStatusHandler> logger) : IOutboxMessageHandler<SendAgencyStatusMessage>
{
    public async Task HandleAsync(SendAgencyStatusMessage message, CancellationToken cancellationToken)
    {
        var key = $"{message.NotificationId:N}.agency-{message.Kind.ToString().ToLowerInvariant()}";
        if (await sentLog.WasSentAsync(key, cancellationToken))
        {
            return;
        }

        var agency = await agencies.GetByIdAsync(message.AgencyId, cancellationToken);
        if (agency is null)
        {
            LogMissingAgency(message.AgencyId);
            return;
        }

        var reason = message.Kind == AgencyNotificationKind.Suspended ? agency.SuspensionReason : null;
        var link = settings.WebBaseUrl is null ? null : $"{settings.WebBaseUrl.TrimEnd('/')}/agency/{agency.Id}";
        var content = AgencyEmails.StatusChanged(message.Kind, agency.Name, agency.ContactEmail, reason, link);
        var delivery = await sender.SendAsync(new EmailMessage(key, content.ToAddress, content.ToName, content.Subject, content.Text, content.Html), cancellationToken);
        sentLog.Record(key, delivery);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        LogSent(message.Kind, agency.Id, delivery);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Agency {Kind} email for agency {AgencyId}: {Delivery}")]
    private partial void LogSent(AgencyNotificationKind kind, Guid agencyId, EmailDelivery delivery);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Agency status email skipped: agency {AgencyId} not found")]
    private partial void LogMissingAgency(Guid agencyId);
}
