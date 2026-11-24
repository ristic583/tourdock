using AwesomeAssertions;
using TourDock.Domain.Common;
using TourDock.Domain.Payments;

namespace TourDock.Domain.UnitTests;

public sealed class RefundTests
{
    private static Payment PaidEur(decimal amount)
    {
        var payment = Payment.Start(Guid.NewGuid(), Guid.NewGuid(), Money.Eur(amount));
        payment.MarkSucceeded(DateTimeOffset.UtcNow);
        return payment;
    }

    [Fact]
    public void Refunds_of_a_payment_never_add_up_to_more_than_was_paid()
    {
        var payment = PaidEur(100m);

        Refund.Create(payment, Money.Eur(60m), "Reduced guests", alreadyRefunded: Money.Eur(40m)).Amount.Amount.Should().Be(60m);
        FluentActions.Invoking(() => Refund.Create(payment, Money.Eur(60.01m), "Too much", alreadyRefunded: Money.Eur(40m)))
            .Should().Throw<InvalidOperationException>();
        FluentActions.Invoking(() => Refund.Create(payment, Money.Zero(), "Nothing", alreadyRefunded: Money.Zero()))
            .Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void A_declined_refund_is_retried_twice_with_a_new_key_then_gives_up()
    {
        var refund = Refund.Create(PaidEur(50m), Money.Eur(50m), "Cancelled", Money.Zero());
        refund.ProviderIdempotencyKey.Should().Be(refund.Id.ToString("N"));

        refund.MarkFailed("Card closed").Should().BeTrue();
        refund.ProviderIdempotencyKey.Should().EndWith("-1");
        refund.MarkFailed("Card closed").Should().BeTrue();
        refund.MarkFailed("Card closed").Should().BeFalse();

        refund.Status.Should().Be(RefundStatus.Failed);
        refund.DomainEvents.Should().ContainSingle(e => e is RefundGaveUp);
        refund.RetryAfterFailure().IsSuccess.Should().BeTrue();
        refund.Status.Should().Be(RefundStatus.Pending);
    }
}
