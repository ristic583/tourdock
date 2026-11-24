using FluentValidation;
using TourDock.Application.Abstractions;
using TourDock.Application.Abstractions.Authentication;
using TourDock.Application.Abstractions.Data;
using TourDock.Application.Abstractions.Messaging;
using TourDock.Domain.Agencies;
using TourDock.Domain.Common;

namespace TourDock.Application.Agencies;

public sealed record RegisterAgencyCommand(string Name, string ContactEmail) : ICommand<AgencyResponse>;

internal sealed class RegisterAgencyValidator : AbstractValidator<RegisterAgencyCommand>
{
    public RegisterAgencyValidator()
    {
        RuleFor(c => c.Name).NotEmpty().MaximumLength(Agency.NameMaxLength);
        RuleFor(c => c.ContactEmail).NotEmpty().EmailAddress().MaximumLength(254);
    }
}

internal sealed class RegisterAgencyHandler(
    IUserContext user, IAgencyRepository agencies, IUnitOfWork unitOfWork, IEmailConfirmationGate emailGate)
    : ICommandHandler<RegisterAgencyCommand, AgencyResponse>
{
    public async Task<Result<AgencyResponse>> Handle(RegisterAgencyCommand command, CancellationToken cancellationToken)
    {
        var confirmed = await emailGate.EnsureConfirmedAsync(user.UserId, cancellationToken);
        if (confirmed.IsFailure)
        {
            return confirmed.Error;
        }

        var agency = Agency.Register(command.Name, command.ContactEmail, user.UserId);
        if (agency.IsFailure)
        {
            return agency.Error;
        }

        agencies.Add(agency.Value);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return agency.Value.ToResponse(user.UserId);
    }
}
