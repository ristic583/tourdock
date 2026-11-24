using TourDock.Application.Abstractions.Data;
using TourDock.Application.Abstractions.Messaging;
using TourDock.Application.Abstractions.Payments;
using TourDock.Application.Bookings;
using TourDock.Application.Common;
using TourDock.Domain.Common;
using TourDock.Domain.Payments;

namespace TourDock.Application.Payments;

/// <param name="Reference">Our id of the payment or refund the event is about.</param>
public sealed record HandlePaymentEventCommand(string EventId, PaymentEventType Type, Guid Reference, string? Reason) : ICommand<Unit>;

/// <summary>
/// Applies a verified provider event. Idempotent: the event id is recorded in the same transaction as the change,
/// so a re-delivered event is a no-op. Events may also arrive out of order; each state change checks the current state.
/// </summary>
internal sealed class HandlePaymentEventHandler(
    IProcessedEventStore processed,
    IPaymentRepository payments,
    IRefundRepository refunds,
    IBookingRepository bookings,
    RefundScheduler refundScheduler,
    IUnitOfWork unitOfWork,
    IOutbox outbox,
    TimeProvider time,
    Observability.TourDockMetrics metrics) : ICommandHandler<HandlePaymentEventCommand, Unit>
{
    private const string Source = "payments";

    public async Task<Result<Unit>> Handle(HandlePaymentEventCommand command, CancellationToken cancellationToken)
    {
        try
        {
            return await ApplyAsync(command, cancellationToken);
        }
        catch (DuplicateKeyException)
        {
            // The same event arrived twice at the same moment and the other delivery recorded it first: already handled.
            unitOfWork.Reset();
            if (await processed.WasProcessedAsync(Source, command.EventId, cancellationToken))
            {
                return Unit.Value;
            }

            throw;
        }
    }

    private Task<Result<Unit>> ApplyAsync(HandlePaymentEventCommand c, CancellationToken cancellationToken) =>
        ConcurrencyRetry.RunAsync(unitOfWork, async () =>
        {
            if (!await processed.TryRecordAsync(Source, c.EventId, cancellationToken))
            {
                return Unit.Value; // already handled
            }

            var result = c.Type switch
            {
                PaymentEventType.PaymentSucceeded => await PaymentSucceededAsync(c.Reference, cancellationToken),
                PaymentEventType.PaymentFailed => await PaymentFailedAsync(c.Reference, c.Reason, cancellationToken),
                _ => await RefundCompletedAsync(c.Reference, c.Type == PaymentEventType.RefundSucceeded, c.Reason, cancellationToken),
            };

            if (result.IsFailure)
            {
                return result.Error;
            }

            await unitOfWork.SaveChangesAsync(cancellationToken);
            return Result.Success(Unit.Value);
        });

    private async Task<Result> PaymentSucceededAsync(Guid paymentId, CancellationToken cancellationToken)
    {
        var payment = await payments.GetByIdAsync(paymentId, cancellationToken);
        if (payment is null)
        {
            return PaymentErrors.UnknownPayment;
        }

        var now = time.GetUtcNow();
        if (!payment.MarkSucceeded(now))
        {
            return Result.Success();
        }

        var booking = await bookings.GetByIdAsync(payment.BookingId, cancellationToken);
        if (booking is null)
        {
            return BookingUseCaseErrors.NotFound;
        }

        // The money arrived, but the booking can't take it (cancelled or expired meanwhile, or already paid by another
        // payment): give it back automatically. A cancelled or expired booking also records it, so it shows as paid and
        // refunded instead of "no refund" / "not charged".
        if (booking.ConfirmPaid(payment.Amount, now).IsFailure)
        {
            _ = booking.RecordLatePayment(payment.Amount); // fails (and records nothing) for an already paid booking
            await refundScheduler.ScheduleAsync(
                payment, payment.Amount, $"Payment received for a {booking.Status.ToString().ToLowerInvariant()} " +
                    "booking", cancellationToken);
        }

        return Result.Success();
    }

    private async Task<Result> PaymentFailedAsync(Guid paymentId, string? reason, CancellationToken cancellationToken)
    {
        var payment = await payments.GetByIdAsync(paymentId, cancellationToken);
        if (payment is null)
        {
            return PaymentErrors.UnknownPayment;
        }

        // The booking stays pending: the customer can try again until the hold expires.
        payment.MarkFailed(reason ?? "Payment failed", time.GetUtcNow());
        metrics.PaymentFailed();
        return Result.Success();
    }

    private async Task<Result> RefundCompletedAsync(Guid refundId, bool succeeded, string? reason, CancellationToken cancellationToken)
    {
        var refund = await refunds.GetByIdAsync(refundId, cancellationToken);
        if (refund is null)
        {
            return PaymentErrors.UnknownRefund;
        }

        if (succeeded)
        {
            refund.MarkSucceeded();
        }
        else if (refund.MarkFailed(reason ?? "Refund failed"))
        {
            // Declined: send it again later under a new idempotency key. After the last attempt it waits for an admin.
            outbox.Enqueue(new IssueRefundMessage(refund.Id), time.GetUtcNow() + Refund.RetryDelay(refund.FailedAttempts));
        }

        return Result.Success();
    }
}
