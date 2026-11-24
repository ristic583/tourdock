using AwesomeAssertions;
using TourDock.Domain.Common;

namespace TourDock.Domain.UnitTests;

public sealed class MoneyTests
{
    [Fact]
    public void Rounds_to_cents_away_from_zero() => Money.Eur(10.005m).Amount.Should().Be(10.01m);

    [Fact]
    public void Rejects_negative_amounts() =>
        FluentActions.Invoking(() => Money.Eur(-1m)).Should().Throw<ArgumentOutOfRangeException>();

    [Fact]
    public void Rejects_unsupported_currency() =>
        FluentActions.Invoking(() => Money.Create(1m, "USD")).Should().Throw<ArgumentException>();

    [Fact]
    public void Allocation_never_loses_a_cent()
    {
        var shares = Money.Eur(100m).Allocate(3);

        shares.Select(s => s.Amount).Should().Equal(33.34m, 33.33m, 33.33m);
        shares.Sum(s => s.Amount).Should().Be(100m);
    }

    [Fact]
    public void Percentage_is_rounded() => Money.Eur(33.33m).Percentage(50).Amount.Should().Be(16.67m);

    [Fact]
    public void Cannot_subtract_below_zero() =>
        FluentActions.Invoking(() => Money.Eur(1m).Subtract(Money.Eur(2m))).Should().Throw<InvalidOperationException>();
}
