using TourDock.Application.Abstractions.Messaging;
using TourDock.Application.Catalog;
using TourDock.Application.Common;
using TourDock.Application.Tours;
using TourDock.Domain.Common;

namespace TourDock.Application.Slots;

public sealed record DeactivateScheduleRuleCommand(Guid AgencyId, Guid TourId, Guid RuleId) : IAgencyRequest, ICommand<TourResponse>,
    IInvalidatesCatalog;

internal sealed class DeactivateScheduleRuleHandler(TourEditor editor) : ICommandHandler<DeactivateScheduleRuleCommand, TourResponse>
{
    public Task<Result<TourResponse>> Handle(DeactivateScheduleRuleCommand command, CancellationToken cancellationToken) =>
        editor.EditAsync(command.AgencyId, command.TourId, (tour, _) => tour.DeactivateScheduleRule(command.RuleId), cancellationToken);
}
