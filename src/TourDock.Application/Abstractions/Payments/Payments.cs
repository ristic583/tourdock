using TourDock.Domain.Common;
using TourDock.Domain.Payments;

namespace TourDock.Application.Abstractions.Payments;

public sealed record ProviderPayment(string ProviderPaymentId, string CheckoutUrl);

public sealed record ProviderRefund(string ProviderRefundId);

/// <summary>
/// A payment provider (a sandbox today; a real one such as Stripe or Adyen could implement the same interface).
/// Every call carries our own id as the idempotency key, so retries after timeouts can never charge or refund twice.
/// </summary>
public interface IPaymentProvider
{
    /// <param name="returnUrl">Where the provider sends the customer after paying or declining (already allow-listed).</param>
    Task<ProviderPayment> CreatePaymentAsync(Guid paymentId, Money amount, string description, Uri? returnUrl, CancellationToken cancellationToken);

    /// <param name="idempotencyKey">Same key on a retry of the same attempt; a new key when re-sending after a decline.</param>
    /// <param name="refundId">Our refund id, echoed back in the provider's webhooks.</param>
    Task<ProviderRefund> CreateRefundAsync(
        Guid refundId, string idempotencyKey, string providerPaymentId, Money amount, CancellationToken cancellationToken);
}

/// <summary>Thrown when the payment provider cannot be reached or fails; the caller may retry safely.</summary>
public sealed class PaymentProviderUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

public interface IPaymentRepository
{
    Task<Payment?> GetByIdAsync(Guid paymentId, CancellationToken cancellationToken);

    /// <summary>The payment in progress for a booking, if any (reused so retries don't create new payments).</summary>
    Task<Payment?> GetPendingForBookingAsync(Guid bookingId, CancellationToken cancellationToken);

    Task<Payment?> GetSucceededForBookingAsync(Guid bookingId, CancellationToken cancellationToken);

    /// <summary>The most recent payment attempt of each booking (bookings without one are left out).</summary>
    Task<IReadOnlyDictionary<Guid, Payment>> GetLatestForBookingsAsync(IEnumerable<Guid> bookingIds, CancellationToken cancellationToken);

    void Add(Payment payment);
}

public interface IRefundRepository
{
    Task<Refund?> GetByIdAsync(Guid refundId, CancellationToken cancellationToken);

    /// <summary>Total of all refunds of a payment, including ones added in the current unit of work and not saved yet.</summary>
    Task<Money> SumForPaymentAsync(Guid paymentId, string currency, CancellationToken cancellationToken);

    /// <summary>Refunds that gave up, oldest first (for admins).</summary>
    Task<IReadOnlyList<Refund>> ListFailedAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<Refund>> ListForBookingAsync(Guid bookingId, CancellationToken cancellationToken);

    void Add(Refund refund);
}

/// <summary>Remembers which provider events were already handled (providers deliver webhooks at least once).</summary>
public interface IProcessedEventStore
{
    /// <summary>Records the event as processed (saved with the rest of the unit of work). False if it was already processed.</summary>
    Task<bool> TryRecordAsync(string source, string eventId, CancellationToken cancellationToken);

    Task<bool> WasProcessedAsync(string source, string eventId, CancellationToken cancellationToken);
}

/// <summary>
/// Decides which return URLs the payment provider may send customers back to. Only allow-listed origins pass,
/// so the checkout can't be abused as an open redirect.
/// </summary>
public interface IReturnUrlPolicy
{
    bool IsAllowed(string url);
}

/// <summary>Verifies that a webhook really came from the payment provider (HMAC signature + timestamp).</summary>
public interface IWebhookSignatureVerifier
{
    bool IsValid(string? signatureHeader, string body);
}
