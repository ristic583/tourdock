namespace TourDock.Domain.Common;

public static class Currencies
{
    /// <summary>The platform currency. All prices, payments and refunds are in EUR (see ADR 0006).</summary>
    public const string Eur = "EUR";

    public static bool IsSupported(string code) => code == Eur;
}

/// <summary>
/// An amount of money in a specific currency. Immutable, never negative, always rounded to 2 decimals
/// with <see cref="MidpointRounding.AwayFromZero"/>.
/// </summary>
public sealed record Money
{
    private const int Decimals = 2;

    private Money(decimal amount, string currency)
    {
        Amount = amount;
        Currency = currency;
    }

    public decimal Amount { get; private set; }

    public string Currency { get; private set; }

    public static Money Zero(string currency = Currencies.Eur) => Create(0m, currency);

    public static Money Eur(decimal amount) => Create(amount, Currencies.Eur);

    public static Money Create(decimal amount, string currency)
    {
        if (!Currencies.IsSupported(currency))
        {
            throw new ArgumentException($"Currency '{currency}' is not supported.", nameof(currency));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(amount);
        return new Money(Round(amount), currency);
    }

    public bool IsZero => Amount == 0m;

    public Money Add(Money other)
    {
        EnsureSameCurrency(other);
        return new Money(Amount + other.Amount, Currency);
    }

    public Money Subtract(Money other)
    {
        EnsureSameCurrency(other);
        if (other.Amount > Amount)
        {
            throw new InvalidOperationException("Money cannot become negative.");
        }

        return new Money(Amount - other.Amount, Currency);
    }

    public Money Multiply(int factor)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(factor);
        return new Money(Amount * factor, Currency);
    }

    /// <summary>Returns <paramref name="percent"/>% of this amount, rounded to cents.</summary>
    public Money Percentage(int percent)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(percent);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(percent, 100);
        return new Money(Round(Amount * percent / 100m), Currency);
    }

    /// <summary>
    /// Splits the amount into <paramref name="parts"/> shares that add up exactly to the original
    /// (e.g. 100.00 / 3 = 33.34 + 33.33 + 33.33). No cent is ever lost.
    /// </summary>
    public IReadOnlyList<Money> Allocate(int parts)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(parts);

        var totalCents = (long)(Amount * 100m);
        var baseShare = totalCents / parts;
        var remainder = totalCents % parts;

        return Enumerable.Range(0, parts)
            .Select(i => new Money((baseShare + (i < remainder ? 1 : 0)) / 100m, Currency))
            .ToList();
    }

    public override string ToString() => $"{Amount:0.00} {Currency}";

    private static decimal Round(decimal value) => Math.Round(value, Decimals, MidpointRounding.AwayFromZero);

    private void EnsureSameCurrency(Money other)
    {
        if (other.Currency != Currency)
        {
            throw new InvalidOperationException($"Cannot combine {Currency} with {other.Currency}.");
        }
    }
}
