using System.Collections.Concurrent;
using TourDock.Application.Abstractions.Payments;
using TourDock.Domain.Common;

namespace TourDock.Api.IntegrationTests;

/// <summary>In-memory payment provider for tests. Honors idempotency keys like a real provider; can be told to fail.</summary>
public sealed class FakePaymentProvider : IPaymentProvider
{
    public ConcurrentDictionary<Guid, (string Id, Money Amount)> Payments { get; } = new();

    public ConcurrentDictionary<Guid, (string Id, string PaymentId, Money Amount)> Refunds { get; } = new();

    public ConcurrentDictionary<Guid, Uri?> ReturnUrls { get; } = new();

    /// <summary>Idempotency keys of every refund request, in order.</summary>
    public ConcurrentQueue<string> RefundKeys { get; } = new();

    public int CreatePaymentCalls;

    /// <summary>Number of upcoming calls that should fail as if the provider were down.</summary>
    public int FailNextCalls;

    public Task<ProviderPayment> CreatePaymentAsync(Guid paymentId, Money amount, string description, Uri? returnUrl, CancellationToken cancellationToken)
    {
        FailIfRequested();
        Interlocked.Increment(ref CreatePaymentCalls);
        ReturnUrls.TryAdd(paymentId, returnUrl);
        var payment = Payments.GetOrAdd(paymentId, _ => ($"pay_{Guid.NewGuid():N}", amount));
        return Task.FromResult(new ProviderPayment(payment.Id, $"http://sandbox.test/checkout/{payment.Id}"));
    }

    public Task<ProviderRefund> CreateRefundAsync(
        Guid refundId, string idempotencyKey, string providerPaymentId, Money amount, CancellationToken cancellationToken)
    {
        FailIfRequested();
        RefundKeys.Enqueue(idempotencyKey);
        var refund = Refunds.GetOrAdd(refundId, _ => ($"re_{Guid.NewGuid():N}", providerPaymentId, amount));
        return Task.FromResult(new ProviderRefund(refund.Id));
    }

    private void FailIfRequested()
    {
        if (Interlocked.Decrement(ref FailNextCalls) >= 0)
        {
            throw new PaymentProviderUnavailableException("Provider down (test)");
        }

        Interlocked.Exchange(ref FailNextCalls, 0);
    }
}
