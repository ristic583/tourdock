using FluentValidation;
using TourDock.Application.Abstractions.Messaging;
using TourDock.Application.Catalog;
using TourDock.Application.Common;
using TourDock.Domain.Agencies;
using TourDock.Domain.Common;
using TourDock.Domain.Tours;

namespace TourDock.Application.Tours;

internal sealed class LocationValidator : AbstractValidator<LocationDto>
{
    public LocationValidator()
    {
        RuleFor(l => l.CountryCode).NotEmpty().Length(2);
        RuleFor(l => l.City).NotEmpty().MaximumLength(100);
        RuleFor(l => l.Address).MaximumLength(250);
        RuleFor(l => l.Latitude).InclusiveBetween(-90, 90);
        RuleFor(l => l.Longitude).InclusiveBetween(-180, 180);
        RuleFor(l => l.TimeZoneId).NotEmpty().MaximumLength(64);
    }
}
