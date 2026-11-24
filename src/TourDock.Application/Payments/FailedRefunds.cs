using FluentValidation;
using TourDock.Application.Abstractions.Data;
using TourDock.Application.Abstractions.Messaging;
using TourDock.Application.Abstractions.Payments;
using TourDock.Application.Common;
using TourDock.Domain.Common;
using TourDock.Domain.Payments;

namespace TourDock.Application.Payments;

/// <summary>A refund the provider declined <see cref="Refund.MaxFailedAttempts"/> times: the customer is still owed it.</summary>
public sealed record FailedRefundResponse(
    Guid RefundId, Guid BookingId, MoneyDto Amount, string Reason, string? FailureReason, int FailedAttempts, DateTimeOffset FailedAt);

public sealed record ListFailedRefundsQuery : IQuery<IReadOnlyList<FailedRefundResponse>>;

/// <param name="Note">Why it is retried now (e.g. "customer updated their card"); goes to the audit log.</param>
public sealed record RetryRefundCommand(Guid RefundId, string Note) : ICommand<Unit>;

internal sealed class RetryRefundValidator : AbstractValidator<RetryRefundCommand>
{
    public RetryRefundValidator() => RuleFor(c => c.Note).NotEmpty().MaximumLength(500);
}

internal sealed class ListFailedRefundsHandler(IRefundRepository refunds)
    : IQueryHandler<ListFailedRefundsQuery, IReadOnlyList<FailedRefundResponse>>
{
    public async Task<Result<IReadOnlyList<FailedRefundResponse>>> Handle(
        ListFailedRefundsQuery query, CancellationToken cancellationToken) =>
        (await refunds.ListFailedAsync(cancellationToken))
        .Select(r => new FailedRefundResponse(r.Id, r.BookingId, r.Amount.ToDto()!, r.Reason, r.FailureReason, r.FailedAttempts, r.UpdatedAt))
        .ToList();
}

/// <summary>Sends a failed refund to the provider once more (new idempotency key), audited.</summary>
internal sealed class RetryRefundHandler(IRefundRepository refunds, IOutbox outbox, AdminAudit audit, IUnitOfWork unitOfWork)
    : ICommandHandler<RetryRefundCommand, Unit>
{
    public async Task<Result<Unit>> Handle(RetryRefundCommand command, CancellationToken cancellationToken)
    {
        var refund = await refunds.GetByIdAsync(command.RefundId, cancellationToken);
        if (refund is null)
        {
            return PaymentErrors.UnknownRefund;
        }

        var retried = refund.RetryAfterFailure();
        if (retried.IsFailure)
        {
            return retried.Error;
        }

        outbox.Enqueue(new IssueRefundMessage(refund.Id));
        audit.Record("Refund.Retry", nameof(Refund), refund.Id, command.Note);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}
