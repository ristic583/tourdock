using FluentValidation;
using TourDock.Application.Abstractions.Messaging;
using TourDock.Application.Catalog;
using TourDock.Application.Common;
using TourDock.Domain.Common;
using TourDock.Domain.Tours;

namespace TourDock.Application.Tours;

public sealed record UpdateTourDetailsCommand(
    Guid AgencyId, Guid TourId, string Title, string Description, LocationDto Location, int DurationMinutes, int MaxGuests)
    : IAgencyRequest, ICommand<TourResponse>, IInvalidatesCatalog;

internal sealed class UpdateTourDetailsValidator : AbstractValidator<UpdateTourDetailsCommand>
{
    public UpdateTourDetailsValidator()
    {
        RuleFor(c => c.Title).NotEmpty().MaximumLength(Tour.TitleMaxLength);
        RuleFor(c => c.Description).NotNull().MaximumLength(Tour.DescriptionMaxLength);
        RuleFor(c => c.Location).NotNull().SetValidator(new LocationValidator());
        RuleFor(c => c.DurationMinutes).InclusiveBetween(15, 14 * 24 * 60);
        RuleFor(c => c.MaxGuests).InclusiveBetween(1, Tour.MaxGuestsLimit);
    }
}

internal sealed class UpdateTourDetailsHandler(TourEditor editor) : ICommandHandler<UpdateTourDetailsCommand, TourResponse>
{
    public Task<Result<TourResponse>> Handle(UpdateTourDetailsCommand command, CancellationToken cancellationToken) =>
        editor.EditAsync(command.AgencyId, command.TourId, (tour, _) =>
        {
            var location = TourInput.ToLocation(command.Location);
            return location.IsFailure
                ? location.Error
                : tour.UpdateDetails(command.Title, command.Description, location.Value, TimeSpan.FromMinutes(command.DurationMinutes),
                    command.MaxGuests);
        }, cancellationToken);
}
