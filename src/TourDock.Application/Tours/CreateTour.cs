using FluentValidation;
using TourDock.Application.Abstractions;
using TourDock.Application.Abstractions.Data;
using TourDock.Application.Abstractions.Messaging;
using TourDock.Application.Catalog;
using TourDock.Application.Common;
using TourDock.Domain.Common;
using TourDock.Domain.Tours;

namespace TourDock.Application.Tours;

public sealed record CreateTourCommand(
    Guid AgencyId,
    string Title,
    string Description,
    LocationDto Location,
    int DurationMinutes,
    int MaxGuests,
    BookingMode BookingMode,
    decimal? PricePerPerson,
    decimal? PrivatePrice,
    IReadOnlyList<RefundTierDto>? CancellationPolicy) : IAgencyRequest, ICommand<TourResponse>, IInvalidatesCatalog;

internal sealed class CreateTourValidator : AbstractValidator<CreateTourCommand>
{
    public CreateTourValidator()
    {
        RuleFor(c => c.Title).NotEmpty().MaximumLength(Tour.TitleMaxLength);
        RuleFor(c => c.Description).NotNull().MaximumLength(Tour.DescriptionMaxLength);
        RuleFor(c => c.Location).NotNull().SetValidator(new LocationValidator());
        RuleFor(c => c.DurationMinutes).InclusiveBetween(15, 14 * 24 * 60);
        RuleFor(c => c.MaxGuests).InclusiveBetween(1, Tour.MaxGuestsLimit);
        RuleFor(c => c.BookingMode).IsInEnum();
        RuleFor(c => c.PricePerPerson).ValidPrice();
        RuleFor(c => c.PrivatePrice).ValidPrice();
    }
}

internal sealed class CreateTourHandler(IAgencyAccess access, ITourRepository tours, IUnitOfWork unitOfWork, IPhotoUrls urls)
    : ICommandHandler<CreateTourCommand, TourResponse>
{
    public async Task<Result<TourResponse>> Handle(CreateTourCommand command, CancellationToken cancellationToken)
    {
        var agency = await access.RequireAsync(command.AgencyId, AgencyAccessLevel.Manage, cancellationToken);
        if (agency.IsFailure)
        {
            return agency.Error;
        }

        var location = TourInput.ToLocation(command.Location);
        if (location.IsFailure)
        {
            return location.Error;
        }

        CancellationPolicy? policy = null;
        if (command.CancellationPolicy is not null)
        {
            var created = TourInput.ToPolicy(command.CancellationPolicy);
            if (created.IsFailure)
            {
                return created.Error;
            }

            policy = created.Value;
        }

        var tour = Tour.Create(
            command.AgencyId, command.Title, command.Description, location.Value, TimeSpan.FromMinutes(command.DurationMinutes),
                command.MaxGuests,
            command.BookingMode, command.PricePerPerson.ToMoney(), command.PrivatePrice.ToMoney(), policy);
        if (tour.IsFailure)
        {
            return tour.Error;
        }

        tours.Add(tour.Value);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return tour.Value.ToResponse(urls);
    }
}
