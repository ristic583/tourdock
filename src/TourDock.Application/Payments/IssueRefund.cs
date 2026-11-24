using TourDock.Application.Abstractions.Data;
using TourDock.Application.Abstractions.Payments;
using TourDock.Domain.Bookings;
using TourDock.Domain.Common;
using TourDock.Domain.Payments;

namespace TourDock.Application.Payments;

/// <summary>Outbox message: ask the provider to refund.</summary>
public sealed record IssueRefundMessage(Guid RefundId) : IOutboxMessage;

/// <summary>Outbox worker step: sends a refund to the provider. Throws on failure so the outbox retries with backoff.</summary>
internal sealed class IssueRefundHandler(
    IRefundRepository refunds, IPaymentRepository payments, IPaymentProvider provider, IUnitOfWork unitOfWork)
    : IOutboxMessageHandler<IssueRefundMessage>
{
    public async Task HandleAsync(IssueRefundMessage message, CancellationToken cancellationToken)
    {
        var refund = await refunds.GetByIdAsync(message.RefundId, cancellationToken);
        if (refund is null || refund.Status != RefundStatus.Pending)
        {
            return; // already sent (or unknown): nothing to do
        }

        var payment = await payments.GetByIdAsync(refund.PaymentId, cancellationToken)
            ?? throw new InvalidOperationException($"Payment {refund.PaymentId} for refund {refund.Id} not found.");

        // Our refund id (plus the attempt number after a decline) is the idempotency key: if this call succeeded but
        // saving failed, the outbox retry gets the same provider refund back.
        var created = await provider.CreateRefundAsync(
            refund.Id, refund.ProviderIdempotencyKey, payment.ProviderPaymentId!, refund.Amount, cancellationToken);
        refund.MarkRequested(created.ProviderRefundId);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
