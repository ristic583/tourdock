using TourDock.Domain.Common;

namespace TourDock.Domain.Tours;

/// <summary>"Cancel at least <see cref="HoursBeforeStart"/> hours before departure and get <see cref="RefundPercent"/>% back."</summary>
public sealed record RefundTier(int HoursBeforeStart, int RefundPercent);

/// <summary>
/// Tiered refund rules. Copied onto every booking at booking time, so later policy changes
/// never affect existing bookings.
/// </summary>
public sealed record CancellationPolicy
{
    private CancellationPolicy(IReadOnlyList<RefundTier> tiers) => Tiers = tiers;

    /// <summary>Ordered from the most generous (largest notice) to the least.</summary>
    public IReadOnlyList<RefundTier> Tiers { get; }

    /// <summary>Full refund up to 72h before, 50% up to 24h before, nothing after that.</summary>
    public static CancellationPolicy Default { get; } = new([new(72, 100), new(24, 50)]);

    public static Result<CancellationPolicy> Create(IEnumerable<RefundTier> tiers)
    {
        var list = tiers.OrderByDescending(t => t.HoursBeforeStart).ToList();

        if (list.Count == 0)
        {
            return TourErrors.InvalidCancellationPolicy("At least one refund tier is required.");
        }

        if (list.Any(t => t.HoursBeforeStart < 0 || t.RefundPercent is < 0 or > 100))
        {
            return TourErrors.InvalidCancellationPolicy("Hours must be >= 0 and refund percent between 0 and 100.");
        }

        if (list.Select(t => t.HoursBeforeStart).Distinct().Count() != list.Count)
        {
            return TourErrors.InvalidCancellationPolicy("Each tier needs a different number of hours.");
        }

        // More notice must never give a smaller refund.
        for (var i = 1; i < list.Count; i++)
        {
            if (list[i].RefundPercent > list[i - 1].RefundPercent)
            {
                return TourErrors.InvalidCancellationPolicy("Refund percent must not increase as departure gets closer.");
            }
        }

        return new CancellationPolicy(list);
    }

    public int RefundPercentFor(TimeSpan timeBeforeStart)
    {
        var hours = timeBeforeStart.TotalHours;
        return Tiers.FirstOrDefault(t => hours >= t.HoursBeforeStart)?.RefundPercent ?? 0;
    }

    public bool Equals(CancellationPolicy? other) => other is not null && Tiers.SequenceEqual(other.Tiers);

    public override int GetHashCode() => Tiers.Aggregate(0, (hash, tier) => HashCode.Combine(hash, tier));
}
