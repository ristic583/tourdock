using TourDock.Application.Abstractions.Data;
using TourDock.Application.Abstractions.Messaging;
using TourDock.Application.Catalog;
using TourDock.Application.Common;
using TourDock.Domain.Common;

namespace TourDock.Application.Tours;

public sealed record ChangeCancellationPolicyCommand(
    Guid AgencyId, Guid TourId, IReadOnlyList<RefundTierDto> Tiers)
    : IAgencyRequest, ICommand<TourResponse>, IInvalidatesCatalog;

internal sealed class ChangeCancellationPolicyHandler(TourEditor editor) : ICommandHandler<ChangeCancellationPolicyCommand, TourResponse>
{
    public Task<Result<TourResponse>> Handle(ChangeCancellationPolicyCommand command, CancellationToken cancellationToken) =>
        editor.EditAsync(command.AgencyId, command.TourId, (tour, _) =>
        {
            var policy = TourInput.ToPolicy(command.Tiers);
            if (policy.IsFailure)
            {
                return policy.Error;
            }

            tour.ChangeCancellationPolicy(policy.Value);
            return Result.Success();
        }, cancellationToken);
}
