using FluentValidation;
using TourDock.Application.Abstractions;
using TourDock.Application.Abstractions.Data;
using TourDock.Application.Abstractions.Messaging;
using TourDock.Application.Abstractions.Payments;
using TourDock.Application.Bookings;
using TourDock.Domain.Bookings;
using TourDock.Domain.Common;
using TourDock.Domain.Payments;

namespace TourDock.Application.Payments;

/// <param name="ReturnUrl">
/// Optional absolute URL on an allow-listed origin; the provider redirects the customer there after paying or declining.
/// Fixed when the provider payment is created: resuming a payment keeps the first return URL.
/// </param>
public sealed record StartPaymentCommand(Guid BookingId, string? ReturnUrl = null) : ICommand<PaymentResponse>;

internal sealed class StartPaymentValidator : AbstractValidator<StartPaymentCommand>
{
    public const int MaxReturnUrlLength = 2000;

    public StartPaymentValidator(IReturnUrlPolicy returnUrls)
    {
        RuleFor(c => c.ReturnUrl)
            .MaximumLength(MaxReturnUrlLength)
            .Must(url => returnUrls.IsAllowed(url!)).WithMessage("The return URL must be an absolute URL on an allowed origin.")
            .When(c => c.ReturnUrl is not null);
    }
}

/// <summary>
/// Creates (or reuses) the payment for a pending booking and returns the provider's checkout URL.
/// The payment row is saved before calling the provider, and our payment id is the provider idempotency key:
/// if the provider call times out, the next attempt reuses the same payment and the provider returns the same result.
/// </summary>
internal sealed class StartPaymentHandler(
    IUserContext user, IBookingRepository bookings, IPaymentRepository payments, IPaymentProvider provider, IUnitOfWork unitOfWork,
    TimeProvider time)
    : ICommandHandler<StartPaymentCommand, PaymentResponse>
{
    public async Task<Result<PaymentResponse>> Handle(StartPaymentCommand command, CancellationToken cancellationToken)
    {
        var booking = await bookings.GetByIdAsync(command.BookingId, cancellationToken);
        if (booking is null || booking.CustomerId != user.UserId)
        {
            return BookingUseCaseErrors.NotFound;
        }

        if (booking.Status != BookingStatus.Pending)
        {
            return PaymentErrors.NotPayable;
        }

        if (booking.IsHoldExpired(time.GetUtcNow()))
        {
            return BookingErrors.HoldExpired;
        }

        var payment = await payments.GetPendingForBookingAsync(booking.Id, cancellationToken);
        if (payment is null)
        {
            payment = Payment.Start(booking.Id, booking.CustomerId, booking.TotalPrice);
            payments.Add(payment);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        if (payment.ProviderPaymentId is null)
        {
            var returnUrl = command.ReturnUrl is null ? null : new Uri(command.ReturnUrl, UriKind.Absolute);
            var created = await provider.CreatePaymentAsync(payment.Id, payment.Amount, $"TourDock booking {booking.Id}", returnUrl,
                cancellationToken);
            payment.AttachProviderPayment(created.ProviderPaymentId, created.CheckoutUrl);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return payment.ToResponse();
    }
}
