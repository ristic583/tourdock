using Microsoft.Extensions.Logging;
using TourDock.Application.Abstractions;
using TourDock.Application.Abstractions.Data;
using TourDock.Domain.Agencies;
using TourDock.Domain.Bookings;
using TourDock.Domain.Common;
using TourDock.Domain.Payments;
using TourDock.Domain.Reviews;

namespace TourDock.Application.Notifications;

public enum NotificationKind
{
    BookingConfirmed,
    BookingCancelledByCustomer,
    BookingCancelledByAgency,
    GuestsReduced,
    RefundPaid,

    /// <summary>The trip is over: ask the traveler for a review.</summary>
    ReviewRequest,

    /// <summary>A traveler reviewed the agency's tour.</summary>
    ReviewPosted,
}

/// <summary>
/// Outbox message for one notification about one booking. <see cref="NotificationId"/> is fixed when the message is created,
/// so every retry builds the same de-duplication keys. Holds ids only, no email addresses: contacts are looked up when sending.
/// </summary>
public sealed record SendNotificationMessage(
    Guid NotificationId, NotificationKind Kind, Guid BookingId, decimal? Amount = null, string? Currency = null, int? Guests = null, Guid? ReviewId = null)
    : IOutboxMessage;

/// <summary>Turns domain events into outbox messages. Called while saving, so both land in one transaction.</summary>
public interface IDomainEventOutboxMapper
{
    IOutboxMessage? Map(IDomainEvent domainEvent);
}

internal sealed class NotificationEventMapper : IDomainEventOutboxMapper
{
    public IOutboxMessage? Map(IDomainEvent domainEvent) => domainEvent switch
    {
        BookingConfirmed e => new SendNotificationMessage(Guid.CreateVersion7(), NotificationKind.BookingConfirmed, e.BookingId),
        BookingCancelled e => new SendNotificationMessage(
            Guid.CreateVersion7(),
            e.By == CancelledBy.Customer ? NotificationKind.BookingCancelledByCustomer : NotificationKind.BookingCancelledByAgency,
            e.BookingId, e.Refund.Amount, e.Refund.Currency),
        BookingGuestsReduced e => new SendNotificationMessage(
            Guid.CreateVersion7(), NotificationKind.GuestsReduced, e.BookingId, e.Refund.Amount, e.Refund.Currency, e.RemovedGuests),
        RefundSucceeded e => new SendNotificationMessage(Guid.CreateVersion7(), NotificationKind.RefundPaid, e.BookingId, e.Amount.Amount, e.Amount.Currency),
        BookingCompleted e => new SendNotificationMessage(Guid.CreateVersion7(), NotificationKind.ReviewRequest, e.BookingId),
        ReviewPosted e => new SendNotificationMessage(Guid.CreateVersion7(), NotificationKind.ReviewPosted, e.BookingId, ReviewId: e.ReviewId),
        AgencyApproved e => new SendAgencyStatusMessage(Guid.CreateVersion7(), AgencyNotificationKind.Approved, e.AgencyId),
        AgencySuspended e => new SendAgencyStatusMessage(Guid.CreateVersion7(), AgencyNotificationKind.Suspended, e.AgencyId),
        AgencyReactivated e => new SendAgencyStatusMessage(Guid.CreateVersion7(), AgencyNotificationKind.Reactivated, e.AgencyId),
        _ => null,
    };
}

/// <summary>
/// Sends the emails for one notification: to the traveler, and for some kinds to the agency's contact address.
/// Each recipient is recorded after delivery, so a retry (the outbox is at-least-once) only sends what is still missing.
/// </summary>
internal sealed partial class SendNotificationHandler(
    IBookingRepository bookings,
    ITourRepository tours,
    IAgencyRepository agencies,
    IReviewRepository reviews,
    IUserDirectory users,
    IEmailSender sender,
    ISentNotificationLog sentLog,
    IUnitOfWork unitOfWork,
    NotificationSettings settings,
    Observability.TourDockMetrics metrics,
    ILogger<SendNotificationHandler> logger) : IOutboxMessageHandler<SendNotificationMessage>
{
    public async Task HandleAsync(SendNotificationMessage message, CancellationToken cancellationToken)
    {
        var booking = await bookings.GetByIdAsync(message.BookingId, cancellationToken);
        if (booking is null)
        {
            LogMissingBooking(message.NotificationId, message.BookingId);
            return;
        }

        var tour = (await tours.GetManyForDisplayAsync([booking.TourId], cancellationToken))[booking.TourId];
        var agency = await agencies.GetByIdAsync(booking.AgencyId, cancellationToken);
        var traveler = (await users.GetContactsAsync([booking.CustomerId], cancellationToken)).GetValueOrDefault(booking.CustomerId);
        var review = message.ReviewId is { } reviewId ? await reviews.GetByIdAsync(reviewId, cancellationToken) : null;
        if (message.Kind == NotificationKind.ReviewPosted && review is null)
        {
            return; // deleted by its author before we got to it
        }

        var details = new BookingEmailDetails(booking, tour, agency?.Name ?? "the agency", traveler?.DisplayName ?? "Traveler", message, settings.WebBaseUrl)
        {
            Review = review,
        };

        if (traveler is not null && BookingEmails.NotifiesTraveler(message.Kind))
        {
            await DeliverAsync(message, "traveler", booking.CustomerId.ToString("N"), BookingEmails.ForTraveler(details, traveler), cancellationToken);
        }

        if (agency is not null && BookingEmails.NotifiesAgency(message.Kind))
        {
            await DeliverAsync(message, "agency", agency.Id.ToString("N"), BookingEmails.ForAgency(details, agency.Name, agency.ContactEmail), cancellationToken);
        }
    }

    private async Task DeliverAsync(SendNotificationMessage message, string role, string recipientId, EmailContent content, CancellationToken cancellationToken)
    {
        var key = $"{message.NotificationId:N}.{role}";
        if (await sentLog.WasSentAsync(key, cancellationToken))
        {
            return;
        }

        var delivery = await sender.SendAsync(
            new EmailMessage(key, content.ToAddress, content.ToName, content.Subject, content.Text, content.Html), cancellationToken);
        sentLog.Record(key, delivery);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        metrics.Email(message.Kind.ToString(), delivery);

        // Ids only: email addresses are personal data and stay out of the logs.
        LogDelivered(message.NotificationId, message.Kind, role, recipientId, delivery);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Notification {NotificationId} ({Kind}) to {Role} {RecipientId}: {Delivery}")]
    private partial void LogDelivered(Guid notificationId, NotificationKind kind, string role, string recipientId, EmailDelivery delivery);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Notification {NotificationId} skipped: booking {BookingId} not found")]
    private partial void LogMissingBooking(Guid notificationId, Guid bookingId);
}
