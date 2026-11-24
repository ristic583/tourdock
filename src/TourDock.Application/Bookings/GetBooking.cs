using TourDock.Application.Abstractions;
using TourDock.Application.Abstractions.Data;
using TourDock.Application.Abstractions.Messaging;
using TourDock.Application.Abstractions.Payments;
using TourDock.Domain.Bookings;
using TourDock.Domain.Common;
using TourDock.Domain.Tours;

namespace TourDock.Application.Bookings;

public sealed record GetBookingQuery(Guid BookingId) : IQuery<BookingResponse>;

internal sealed class GetBookingHandler(
    IUserContext user, IBookingRepository bookings, ITourRepository tours, IPaymentRepository payments, BookingMapper mapper,
    TimeProvider time)
    : IQueryHandler<GetBookingQuery, BookingResponse>
{
    public async Task<Result<BookingResponse>> Handle(GetBookingQuery query, CancellationToken cancellationToken)
    {
        var booking = await bookings.GetByIdAsync(query.BookingId, cancellationToken);
        if (booking is null || booking.CustomerId != user.UserId)
        {
            return BookingUseCaseErrors.NotFound;
        }

        var tour = (await tours.GetManyForDisplayAsync([booking.TourId], cancellationToken))[booking.TourId];
        var payment = (await payments.GetLatestForBookingsAsync([booking.Id], cancellationToken)).GetValueOrDefault(booking.Id);
        return mapper.ToResponse(booking, tour, time.GetUtcNow(), payment);
    }
}
