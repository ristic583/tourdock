using TourDock.Domain.Common;
using TourDock.Domain.Slots;
using TourDock.Domain.Tours;

namespace TourDock.Domain.UnitTests;

internal static class TestData
{
    public static readonly DateTimeOffset Now = new(2026, 6, 1, 8, 0, 0, TimeSpan.Zero);

    public static Location Split => Location.Create("HR", "Split", "Riva 1", 43.508, 16.440, "Europe/Zagreb").Value;

    public static Tour Tour(
        BookingMode mode = BookingMode.Both,
        int maxGuests = 10,
        decimal perPerson = 50m,
        decimal privatePrice = 400m,
        CancellationPolicy? policy = null)
    {
        var tour = Domain.Tours.Tour.Create(
            Guid.NewGuid(),
            "Sunset Cruise",
            "Three hours along the coast.",
            Split,
            TimeSpan.FromHours(3),
            maxGuests,
            mode,
            Tours.Tour.AllowsShared(mode) ? Money.Eur(perPerson) : null,
            Tours.Tour.AllowsPrivate(mode) ? Money.Eur(privatePrice) : null,
            policy).Value;
        tour.Publish();
        return tour;
    }

    public static Slot Slot(Tour tour, int vehicleCapacity = 12, TimeSpan? startsIn = null) =>
        Domain.Slots.Slot.Create(tour, Guid.NewGuid(), vehicleCapacity, Now + (startsIn ?? TimeSpan.FromDays(7)), Now).Value;
}
