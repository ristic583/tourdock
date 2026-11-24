using System.Security.Cryptography;
using System.Text;
using TourDock.Application.Abstractions;
using TourDock.Application.Abstractions.Messaging;
using TourDock.Application.Bookings;
using TourDock.Application.Payments;
using TourDock.Domain.Bookings;

namespace TourDock.Application.Assistant;

/// <summary>
/// What an agent may do for a signed-in traveler: list bookings, hold seats and hand over the checkout link, cancel
/// after an explicit yes. Every call goes through the same handlers as the web app, as the signed-in user.
/// </summary>
internal sealed class TravelerTools(
    IQueryHandler<ListMyBookingsQuery, IReadOnlyList<BookingResponse>> myBookings,
    IQueryHandler<GetBookingQuery, BookingResponse> booking,
    ICommandHandler<CreateBookingCommand, CreateBookingResult> create,
    ICommandHandler<StartPaymentCommand, PaymentResponse> pay,
    ICommandHandler<CancelBookingCommand, CancellationResult> cancel,
    IUserContext user,
    NotificationSettings settings,
    TimeProvider time) : IAgentToolGroup
{
    public const string ListMyBookings = "list_my_bookings";
    public const string StartBooking = "start_booking";
    public const string CancelBooking = "cancel_booking";

    public IReadOnlyList<AgentToolDefinition> Definitions => AccountToolSchemas.Traveler;

    public async Task<AgentToolResult> RunAsync(string name, AgentInput input, CancellationToken cancellationToken) => name switch
    {
        ListMyBookings => await ListAsync(cancellationToken),
        StartBooking => await StartAsync(input, cancellationToken),
        CancelBooking => await CancelAsync(input, cancellationToken),
        _ => AgentToolResult.Failure($"Unknown tool '{name}'."),
    };

    private async Task<AgentToolResult> ListAsync(CancellationToken cancellationToken)
    {
        var result = await myBookings.Handle(new ListMyBookingsQuery(), cancellationToken);
        return result.IsFailure
            ? AgentErrors.From(result.Error!)
            : AccountToolText.Result(result.Value.Count == 0
                ? "No bookings yet."
                : string.Join("\n", result.Value.Select(AccountToolText.Booking)));
    }

    private async Task<AgentToolResult> StartAsync(AgentInput input, CancellationToken cancellationToken)
    {
        var departureId = input.RequiredGuid("departure_id", "get_departures");
        var guests = input.Integer("guests") ?? throw new AgentInputException("'guests' is required.");
        var type = Enum.TryParse<BookingType>(input.RequiredText("booking_type"), ignoreCase: true, out var t)
            ? t
            : throw new AgentInputException("'booking_type' must be Shared or Private.");

        var created = await create.Handle(new CreateBookingCommand(departureId, type, guests, IdempotencyKey(departureId, type, guests)),
            cancellationToken);
        if (created.IsFailure)
        {
            return AgentErrors.From(created.Error!);
        }

        var held = created.Value.Booking;
        var returnUrl = settings.WebBaseUrl is { } web ? $"{web.TrimEnd('/')}/bookings" : null;
        var payment = await pay.Handle(new StartPaymentCommand(held.Id, returnUrl), cancellationToken);
        return payment.IsFailure
            ? AgentErrors.From(payment.Error!)
            : AccountToolText.Result(AccountToolText.Held(held, payment.Value.CheckoutUrl));
    }

    private async Task<AgentToolResult> CancelAsync(AgentInput input, CancellationToken cancellationToken)
    {
        var bookingId = input.RequiredGuid("booking_id", "list_my_bookings");
        if (!input.Boolean("confirm"))
        {
            var preview = await booking.Handle(new GetBookingQuery(bookingId), cancellationToken);
            return preview.IsFailure
                ? AgentErrors.From(preview.Error!)
                : AccountToolText.Result(AccountToolText.CancelPreview(preview.Value));
        }

        var result = await cancel.Handle(new CancelBookingCommand(bookingId, "Cancelled through an AI assistant"), cancellationToken);
        return result.IsFailure ? AgentErrors.From(result.Error!) : AccountToolText.Result(AccountToolText.Cancelled(result.Value));
    }

    /// <summary>
    /// The same request from the same traveler on the same day gets the same key, so an agent that retries (or asks
    /// twice) gets the booking it already made instead of a second hold.
    /// </summary>
    private string IdempotencyKey(Guid departureId, BookingType type, int guests)
    {
        var day = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
        var seed = $"{user.UserId}|{departureId}|{type}|{guests}|{day:yyyy-MM-dd}";
        return "agent-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(seed)))[..40];
    }
}
