using System.Diagnostics.Metrics;
using TourDock.Application.Abstractions;
using TourDock.Domain.Bookings;
using TourDock.Domain.Common;
using TourDock.Domain.Payments;
using TourDock.Domain.Reviews;

namespace TourDock.Application.Observability;

/// <summary>
/// Business metrics (plain System.Diagnostics.Metrics, exported by OpenTelemetry when it's configured). Booking and
/// review counters are driven by domain events in one place, so every code path that books or cancels is counted.
/// </summary>
public sealed class TourDockMetrics
{
    public const string MeterName = "TourDock";

    private readonly Counter<long> _bookingsCreated;
    private readonly Counter<long> _bookingsConfirmed;
    private readonly Counter<long> _bookingsCancelled;
    private readonly Counter<long> _bookingsCompleted;
    private readonly Counter<long> _refundsSucceeded;
    private readonly Counter<long> _refundsGaveUp;
    private readonly Counter<long> _reviewsPosted;
    private readonly Counter<long> _paymentsFailed;
    private readonly Counter<long> _emails;
    private readonly Counter<long> _assistantTokens;
    private long _outboxPending;
    private long _outboxParked;

    public TourDockMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(MeterName);
        _bookingsCreated = meter.CreateCounter<long>("tourdock.bookings.created", description: "Seat holds created (pending payment)");
        _bookingsConfirmed = meter.CreateCounter<long>("tourdock.bookings.confirmed", description: "Bookings paid and confirmed");
        _bookingsCancelled = meter.CreateCounter<long>("tourdock.bookings.cancelled", description: "Bookings cancelled, by who cancelled");
        _bookingsCompleted = meter.CreateCounter<long>("tourdock.bookings.completed", description: "Departures that took place");
        _refundsSucceeded = meter.CreateCounter<long>("tourdock.refunds.succeeded", description: "Refunds the provider confirmed");
        _refundsGaveUp = meter.CreateCounter<long>("tourdock.refunds.failed", description: "Refunds the provider declined too often; need an admin");
        _reviewsPosted = meter.CreateCounter<long>("tourdock.reviews.posted", description: "Reviews posted, by rating");
        _paymentsFailed = meter.CreateCounter<long>("tourdock.payments.failed", description: "Declined or failed payment attempts");
        _emails = meter.CreateCounter<long>("tourdock.emails", description: "Notification emails, by kind and delivery result");
        _assistantTokens = meter.CreateCounter<long>(
            "tourdock.assistant.tokens", description: "Model tokens the assistant used, by model and kind");
        meter.CreateObservableGauge("tourdock.outbox.pending", () => Interlocked.Read(ref _outboxPending), description: "Outbox messages waiting to be delivered");
        meter.CreateObservableGauge("tourdock.outbox.parked", () => Interlocked.Read(ref _outboxParked), description: "Outbox messages that gave up after too many attempts (last 7 days)");
    }

    public void BookingsCreated(int count)
    {
        if (count > 0)
        {
            _bookingsCreated.Add(count);
        }
    }

    public void PaymentFailed() => _paymentsFailed.Add(1);

    /// <summary>Tokens of one model call, by kind (input, output, cache_read, cache_write): what the assistant costs.</summary>
    public void AssistantTokens(string model, long input, long output, long cacheRead, long cacheWrite)
    {
        var counts = new[] { ("input", input), ("output", output), ("cache_read", cacheRead), ("cache_write", cacheWrite) };
        foreach (var (kind, count) in counts)
        {
            _assistantTokens.Add(
                count, new KeyValuePair<string, object?>("model", model), new KeyValuePair<string, object?>("kind", kind));
        }
    }

    public void Email(string kind, EmailDelivery delivery) =>
        _emails.Add(1, new KeyValuePair<string, object?>("kind", kind), new KeyValuePair<string, object?>("delivery", delivery.ToString()));

    public void OutboxState(long pending, long parked)
    {
        Interlocked.Exchange(ref _outboxPending, pending);
        Interlocked.Exchange(ref _outboxParked, parked);
    }

    /// <summary>Counts what a domain event means for the business. Called when the event is saved.</summary>
    public void Record(IDomainEvent domainEvent)
    {
        switch (domainEvent)
        {
            case BookingConfirmed:
                _bookingsConfirmed.Add(1);
                break;
            case BookingCancelled e:
                _bookingsCancelled.Add(1, new KeyValuePair<string, object?>("by", e.By.ToString()));
                break;
            case BookingCompleted:
                _bookingsCompleted.Add(1);
                break;
            case RefundSucceeded:
                _refundsSucceeded.Add(1);
                break;
            case RefundGaveUp:
                _refundsGaveUp.Add(1);
                break;
            case ReviewPosted:
                _reviewsPosted.Add(1);
                break;
        }
    }
}
