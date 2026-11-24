using TourDock.Domain.Common;

namespace TourDock.Domain.Payments;

public enum PaymentStatus
{
    /// <summary>Created at the provider; waiting for the customer to pay.</summary>
    Pending,
    Succeeded,
    Failed,
}

/// <summary>One attempt to pay for a booking through the payment provider.</summary>
public sealed class Payment : Entity, IAuditable
{
    private Payment(Guid id)
        : base(id) { }

    private Payment() { }

    public Guid BookingId { get; private set; }

    public Guid CustomerId { get; private set; }

    public Money Amount { get; private set; } = null!;

    public PaymentStatus Status { get; private set; }

    /// <summary>The provider's id for this payment (set once the provider accepted it).</summary>
    public string? ProviderPaymentId { get; private set; }

    /// <summary>Where the customer completes the payment (provider-hosted page).</summary>
    public string? CheckoutUrl { get; private set; }

    public string? FailureReason { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public uint Version { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static Payment Start(Guid bookingId, Guid customerId, Money amount) =>
        new(NewId()) { BookingId = bookingId, CustomerId = customerId, Amount = amount, Status = PaymentStatus.Pending };

    public void AttachProviderPayment(string providerPaymentId, string checkoutUrl)
    {
        ProviderPaymentId = providerPaymentId;
        CheckoutUrl = checkoutUrl;
    }

    /// <returns>False if the payment was already completed (duplicate or out-of-order notification).</returns>
    public bool MarkSucceeded(DateTimeOffset now)
    {
        if (Status != PaymentStatus.Pending)
        {
            return false;
        }

        Status = PaymentStatus.Succeeded;
        CompletedAt = now;
        return true;
    }

    public bool MarkFailed(string reason, DateTimeOffset now)
    {
        if (Status != PaymentStatus.Pending)
        {
            return false;
        }

        Status = PaymentStatus.Failed;
        FailureReason = reason;
        CompletedAt = now;
        return true;
    }
}

public enum RefundStatus
{
    /// <summary>Recorded and queued (outbox); not yet accepted by the provider.</summary>
    Pending,

    /// <summary>The provider accepted the refund request.</summary>
    Requested,
    Succeeded,
    Failed,
}

/// <summary>Money going back to a customer for a (partial) cancellation. Sent to the provider by the outbox worker.</summary>
public sealed class Refund : Entity, IAuditable
{
    private Refund(Guid id)
        : base(id) { }

    private Refund() { }

    public Guid BookingId { get; private set; }

    public Guid PaymentId { get; private set; }

    public Money Amount { get; private set; } = null!;

    public RefundStatus Status { get; private set; }

    public string Reason { get; private set; } = null!;

    public string? ProviderRefundId { get; private set; }

    public string? FailureReason { get; private set; }

    /// <summary>How many times the provider declined this refund. Each retry is sent under a new idempotency key.</summary>
    public int FailedAttempts { get; private set; }

    public uint Version { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Provider declines before a refund gives up and waits for an admin.</summary>
    public const int MaxFailedAttempts = 3;

    /// <summary>Wait before re-sending after the n-th decline (a closed card or a provider hiccup rarely clears in seconds).</summary>
    public static TimeSpan RetryDelay(int failedAttempts) => failedAttempts <= 1 ? TimeSpan.FromMinutes(15) : TimeSpan.FromHours(2);

    /// <summary>Idempotency key for the current attempt: the first keeps the plain id, retries get a suffix.</summary>
    public string ProviderIdempotencyKey => FailedAttempts == 0 ? Id.ToString("N") : $"{Id:N}-{FailedAttempts}";

    /// <param name="alreadyRefunded">Earlier refunds of the same payment (any status: a failed one is still owed).</param>
    public static Refund Create(Payment payment, Money amount, string reason, Money alreadyRefunded)
    {
        if (payment.Status != PaymentStatus.Succeeded)
        {
            throw new InvalidOperationException("Only a succeeded payment can be refunded.");
        }

        // Backstop for the booking's own refund rules: never give back more than was paid.
        if (amount.IsZero || amount.Amount + alreadyRefunded.Amount > payment.Amount.Amount || amount.Currency != payment.Amount.Currency)
        {
            throw new InvalidOperationException(
                $"Refund of {amount.Amount} {amount.Currency} is not possible: paid {payment.Amount.Amount}, already refunded {alreadyRefunded.Amount}.");
        }

        return new Refund(NewId()) { BookingId = payment.BookingId, PaymentId = payment.Id, Amount = amount, Reason = reason, Status = RefundStatus.Pending };
    }

    public void MarkRequested(string providerRefundId)
    {
        if (Status == RefundStatus.Pending)
        {
            Status = RefundStatus.Requested;
            ProviderRefundId = providerRefundId;
        }
    }

    public void MarkSucceeded()
    {
        if (Status is RefundStatus.Pending or RefundStatus.Requested)
        {
            Status = RefundStatus.Succeeded;
            Raise(new RefundSucceeded(Id, BookingId, Amount));
        }
    }

    /// <summary>
    /// The provider declined the refund. Below <see cref="MaxFailedAttempts"/> it goes back to pending so it can be sent
    /// again; after that it stays failed until an admin retries it. Returns whether a retry should be scheduled.
    /// </summary>
    public bool MarkFailed(string reason)
    {
        if (Status is not (RefundStatus.Pending or RefundStatus.Requested))
        {
            return false;
        }

        FailedAttempts++;
        FailureReason = reason;
        ProviderRefundId = null;
        if (FailedAttempts < MaxFailedAttempts)
        {
            Status = RefundStatus.Pending;
            return true;
        }

        Status = RefundStatus.Failed;
        Raise(new RefundGaveUp(Id, BookingId, Amount, reason));
        return false;
    }

    /// <summary>An admin sends a refund that gave up once more (e.g. after the customer updated their card).</summary>
    public Result RetryAfterFailure()
    {
        if (Status != RefundStatus.Failed)
        {
            return Error.Conflict("Refund.NotFailed", "Only a failed refund can be retried.");
        }

        Status = RefundStatus.Pending;
        return Result.Success();
    }
}

/// <summary>The provider declined a refund too many times: the customer's money is stuck until someone acts.</summary>
public sealed record RefundGaveUp(Guid RefundId, Guid BookingId, Money Amount, string Reason) : IDomainEvent;

/// <summary>The provider confirmed the money is on its way back to the customer.</summary>
public sealed record RefundSucceeded(Guid RefundId, Guid BookingId, Money Amount) : IDomainEvent;
