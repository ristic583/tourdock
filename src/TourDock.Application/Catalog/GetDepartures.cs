using FluentValidation;
using TourDock.Application.Abstractions.Messaging;
using TourDock.Application.TravelInfo;
using TourDock.Domain.Common;
using TourDock.Domain.Tours;

namespace TourDock.Application.Catalog;

public sealed record GetDeparturesQuery(Guid TourId, DateOnly From, DateOnly To, int Guests) : IQuery<IReadOnlyList<DepartureResponse>>;

internal sealed class GetDeparturesValidator : AbstractValidator<GetDeparturesQuery>
{
    public GetDeparturesValidator()
    {
        RuleFor(q => q.To).GreaterThanOrEqualTo(q => q.From);
        RuleFor(q => q).Must(q => q.To.DayNumber - q.From.DayNumber <= 62).WithName("to").WithMessage("The date range must be at most 62 " +
            "days.");
        RuleFor(q => q.Guests).InclusiveBetween(1, Tour.MaxGuestsLimit);
    }
}

internal sealed class GetDeparturesHandler(ITourCatalog catalog) : IQueryHandler<GetDeparturesQuery, IReadOnlyList<DepartureResponse>>
{
    public async Task<Result<IReadOnlyList<DepartureResponse>>> Handle(GetDeparturesQuery query, CancellationToken cancellationToken) =>
        await catalog.GetDeparturesAsync(query,
            cancellationToken) is { } departures ? Result.Success(departures) : CatalogErrors.TourNotFound;
}
