using FluentValidation;
using TourDock.Application.Abstractions.Data;
using TourDock.Application.Abstractions.Messaging;
using TourDock.Application.Common;
using TourDock.Domain.Bookings;
using TourDock.Domain.Common;

namespace TourDock.Application.Slots;

public sealed record ListSlotsQuery(Guid AgencyId, DateOnly From, DateOnly To, Guid? TourId) : IAgencyRequest,
    IQuery<IReadOnlyList<SlotResponse>>;

internal sealed class ListSlotsValidator : AbstractValidator<ListSlotsQuery>
{
    public const int MaxRangeDays = 120;

    public ListSlotsValidator()
    {
        RuleFor(q => q.To).GreaterThanOrEqualTo(q => q.From);
        RuleFor(q => q).Must(q => q.To.DayNumber - q.From.DayNumber <= MaxRangeDays)
            .WithName("to").WithMessage($"The date range must be at most {MaxRangeDays} days.");
    }
}

internal sealed class ListSlotsHandler(
    IAgencyAccess access, ISlotRepository slots)
    : IQueryHandler<ListSlotsQuery, IReadOnlyList<SlotResponse>>
{
    public async Task<Result<IReadOnlyList<SlotResponse>>> Handle(ListSlotsQuery query, CancellationToken cancellationToken)
    {
        var agency = await access.RequireAsync(query.AgencyId, AgencyAccessLevel.Read, cancellationToken);
        if (agency.IsFailure)
        {
            return agency.Error;
        }

        // Pad by a day on each side so local dates in any time zone are covered, then filter by local date.
        var from = new DateTimeOffset(query.From.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).AddDays(-1);
        var to = new DateTimeOffset(query.To.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).AddDays(2);
        var list = await slots.ListAsync(query.AgencyId, from, to, query.TourId, cancellationToken);
        return list.Where(s => s.LocalDate >= query.From && s.LocalDate <= query.To).Select(s => s.ToResponse()).ToList();
    }
}
