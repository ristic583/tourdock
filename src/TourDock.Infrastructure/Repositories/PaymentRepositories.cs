using Microsoft.EntityFrameworkCore;
using TourDock.Application.Abstractions.Payments;
using TourDock.Domain.Common;
using TourDock.Domain.Payments;
using TourDock.Infrastructure.Persistence;

namespace TourDock.Infrastructure.Repositories;

internal sealed class PaymentRepository(TourDockDbContext db) : IPaymentRepository
{
    public Task<Payment?> GetByIdAsync(Guid paymentId, CancellationToken cancellationToken) =>
        db.Payments.SingleOrDefaultAsync(p => p.Id == paymentId, cancellationToken);

    public Task<Payment?> GetPendingForBookingAsync(Guid bookingId, CancellationToken cancellationToken) =>
        db.Payments.SingleOrDefaultAsync(p => p.BookingId == bookingId && p.Status == PaymentStatus.Pending, cancellationToken);

    public Task<Payment?> GetSucceededForBookingAsync(Guid bookingId, CancellationToken cancellationToken) =>
        db.Payments.FirstOrDefaultAsync(p => p.BookingId == bookingId && p.Status == PaymentStatus.Succeeded, cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, Payment>> GetLatestForBookingsAsync(IEnumerable<Guid> bookingIds, CancellationToken cancellationToken)
    {
        var ids = bookingIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return new Dictionary<Guid, Payment>();
        }

        // Read-only: no tracking, so this never interferes with a payment the caller is changing.
        var latest = await db.Payments.AsNoTracking()
            .Where(p => ids.Contains(p.BookingId))
            .GroupBy(p => p.BookingId)
            .Select(g => g.OrderByDescending(p => p.CreatedAt).ThenByDescending(p => p.Id).First())
            .ToListAsync(cancellationToken);
        return latest.ToDictionary(p => p.BookingId);
    }

    public void Add(Payment payment) => db.Payments.Add(payment);
}

internal sealed class RefundRepository(TourDockDbContext db) : IRefundRepository
{
    public Task<Refund?> GetByIdAsync(Guid refundId, CancellationToken cancellationToken) =>
        db.Refunds.SingleOrDefaultAsync(r => r.Id == refundId, cancellationToken);

    public async Task<Money> SumForPaymentAsync(Guid paymentId, string currency, CancellationToken cancellationToken)
    {
        var saved = await db.Refunds.Where(r => r.PaymentId == paymentId).Select(r => r.Amount.Amount).SumAsync(cancellationToken);
        var unsaved = db.Refunds.Local
            .Where(r => r.PaymentId == paymentId && db.Entry(r).State == EntityState.Added)
            .Sum(r => r.Amount.Amount);
        return Money.Create(saved + unsaved, currency);
    }

    public async Task<IReadOnlyList<Refund>> ListFailedAsync(CancellationToken cancellationToken) =>
        await db.Refunds.Where(r => r.Status == RefundStatus.Failed).OrderBy(r => r.UpdatedAt).ThenBy(r => r.Id).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Refund>> ListForBookingAsync(Guid bookingId, CancellationToken cancellationToken) =>
        await db.Refunds.Where(r => r.BookingId == bookingId).OrderBy(r => r.CreatedAt).ToListAsync(cancellationToken);

    public void Add(Refund refund) => db.Refunds.Add(refund);
}
