using TourDock.Domain.Common;

namespace TourDock.Domain.Tours;

/// <summary>Overrides the tour's base prices for departures whose local date falls in [From, To] (inclusive).</summary>
public sealed class SeasonalPrice : Entity
{
    internal SeasonalPrice(Guid tourId, DateOnly from, DateOnly to, Money? pricePerPerson, Money? privatePrice)
        : base(NewId())
    {
        TourId = tourId;
        From = from;
        To = to;
        PricePerPerson = pricePerPerson;
        PrivatePrice = privatePrice;
    }

    private SeasonalPrice() { }

    public Guid TourId { get; private set; }

    public DateOnly From { get; private set; }

    public DateOnly To { get; private set; }

    public Money? PricePerPerson { get; private set; }

    public Money? PrivatePrice { get; private set; }

    public bool Covers(DateOnly date) => date >= From && date <= To;

    public bool Overlaps(DateOnly from, DateOnly to) => from <= To && to >= From;
}
