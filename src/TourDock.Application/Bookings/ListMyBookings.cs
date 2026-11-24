using TourDock.Application.Abstractions;
using TourDock.Application.Abstractions.Data;
using TourDock.Application.Abstractions.Messaging;
using TourDock.Application.Abstractions.Payments;
using TourDock.Domain.Bookings;
using TourDock.Domain.Common;
using TourDock.Domain.Tours;

namespace TourDock.Application.Bookings;

public sealed record ListMyBookingsQuery : IQuery<IReadOnlyList<BookingResponse>>;

internal sealed class ListMyBookingsHandler(
    IUserContext user, IBookingRepository bookings, ITourRepository tours, IPaymentRepository payments, BookingMapper mapper,
    TimeProvider time)
    : IQueryHandler<ListMyBookingsQuery, IReadOnlyList<BookingResponse>>
{
    public async Task<Result<IReadOnlyList<BookingResponse>>> Handle(ListMyBookingsQuery query, CancellationToken cancellationToken)
    {
        var list = await bookings.ListForCustomerAsync(user.UserId, cancellationToken);
        var tourMap = await tours.GetManyForDisplayAsync(list.Select(b => b.TourId), cancellationToken);
        var latest = await payments.GetLatestForBookingsAsync(list.Select(b => b.Id), cancellationToken);
        var now = time.GetUtcNow();
        return list.Select(b => mapper.ToResponse(b, tourMap[b.TourId], now, latest.GetValueOrDefault(b.Id))).ToList();
    }
}
