using FluentValidation;
using TourDock.Application.Abstractions;
using TourDock.Application.Abstractions.Data;
using TourDock.Application.Abstractions.Messaging;
using TourDock.Application.Abstractions.Payments;
using TourDock.Application.Common;
using TourDock.Domain.Common;
using TourDock.Domain.Tours;

namespace TourDock.Application.Bookings;

public sealed record ListAgencyBookingsQuery(Guid AgencyId, DateOnly From, DateOnly To) : IAgencyRequest,
    IQuery<IReadOnlyList<AgencyBookingResponse>>;

internal sealed class ListAgencyBookingsValidator : AbstractValidator<ListAgencyBookingsQuery>
{
    public const int MaxRangeDays = 120;

    public ListAgencyBookingsValidator()
    {
        RuleFor(q => q.To).GreaterThanOrEqualTo(q => q.From);
        RuleFor(q => q).Must(q => q.To.DayNumber - q.From.DayNumber <= MaxRangeDays)
            .WithName("to").WithMessage($"The date range must be at most {MaxRangeDays} days.");
    }
}

internal sealed class ListAgencyBookingsHandler(
    IAgencyAccess access, IBookingRepository bookings, ITourRepository tours, IUserDirectory users, IPaymentRepository payments,
    BookingMapper mapper, TimeProvider time)
    : IQueryHandler<ListAgencyBookingsQuery, IReadOnlyList<AgencyBookingResponse>>
{
    public async Task<Result<IReadOnlyList<AgencyBookingResponse>>> Handle(
        ListAgencyBookingsQuery query, CancellationToken cancellationToken)
    {
        var agency = await access.RequireAsync(query.AgencyId, AgencyAccessLevel.Read, cancellationToken);
        if (agency.IsFailure)
        {
            return agency.Error;
        }

        var from = new DateTimeOffset(query.From.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).AddDays(-1);
        var to = new DateTimeOffset(query.To.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).AddDays(2);
        var list = await bookings.ListForAgencyAsync(query.AgencyId, from, to, cancellationToken);
        var tourMap = await tours.GetManyForDisplayAsync(list.Select(b => b.TourId), cancellationToken);
        var people = await users.GetContactsAsync(list.Select(b => b.CustomerId), cancellationToken);
        var latest = await payments.GetLatestForBookingsAsync(list.Select(b => b.Id), cancellationToken);
        var now = time.GetUtcNow();

        return list.Select(b =>
        {
            var contact = people.GetValueOrDefault(b.CustomerId) ?? new UserContact("Unknown", string.Empty);
            return new AgencyBookingResponse(mapper.ToResponse(b, tourMap[b.TourId], now, latest.GetValueOrDefault(b.Id)),
                contact.DisplayName, contact.Email);
        }).ToList();
    }
}
