using Microsoft.AspNetCore.Mvc;
using TourDock.Api.Infrastructure;
using TourDock.Application.Abstractions.Messaging;
using TourDock.Application.Bookings;
using TourDock.Domain.Bookings;

namespace TourDock.Api.Endpoints;

internal static class BookingEndpoints
{
    public const string IdempotencyHeader = "Idempotency-Key";

    public static RouteGroupBuilder MapBookingEndpoints(this RouteGroupBuilder api)
    {
        MapTravelerBookings(api);
        MapAgencyBookings(api);
        return api;
    }

    private static void MapTravelerBookings(RouteGroupBuilder api)
    {
        var bookings = api.MapGroup("/bookings").WithTags("Bookings").RequireAuthorization();

        bookings.MapPost("/", async (
                [FromHeader(Name = IdempotencyHeader)] string? idempotencyKey,
                CreateBookingRequest body,
                ICommandHandler<CreateBookingCommand, CreateBookingResult> handler,
                HttpContext http,
                CancellationToken ct) =>
            {
                var result = await handler.Handle(
                    new CreateBookingCommand(body.DepartureId, body.Type, body.Guests, idempotencyKey ?? string.Empty), ct);
                return result.ToHttp(r =>
                {
                    if (r.Replayed)
                    {
                        // Same key as an earlier request: return the original booking instead of creating a second one.
                        http.Response.Headers["Idempotent-Replayed"] = "true";
                        return TypedResults.Ok(r.Booking);
                    }

                    return TypedResults.Created($"/api/v1/bookings/{r.Booking.Id}", r.Booking);
                });
            })
            .Produces<BookingResponse>(StatusCodes.Status201Created)
            .Produces<BookingResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .RequireRateLimiting(Hardening.WritePolicy)
            .WithSummary("Book seats or a whole departure. Requires an Idempotency-Key header (e.g. a UUID) so retries are safe.");

        bookings.MapGet("/{bookingId:guid}", async (Guid bookingId, IQueryHandler<GetBookingQuery, BookingResponse> h,
            CancellationToken ct) =>
                (await h.Handle(new GetBookingQuery(bookingId), ct)).ToHttp())
            .Produces<BookingResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("One of your bookings, including what you'd get back if you cancelled now and the last payment attempt");

        bookings.MapPost("/{bookingId:guid}/cancel", async (Guid bookingId, CancelRequest? body,
            ICommandHandler<CancelBookingCommand, CancellationResult> h, CancellationToken ct) =>
                (await h.Handle(new CancelBookingCommand(bookingId, body?.Reason), ct)).ToHttp())
            .Produces<CancellationResult>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithSummary("Cancel your booking; refund follows the cancellation policy");

        bookings.MapPost("/{bookingId:guid}/reduce-guests", async (Guid bookingId, ReduceGuestsRequest body,
            ICommandHandler<ReduceGuestsCommand, CancellationResult> h, CancellationToken ct) =>
                (await h.Handle(new ReduceGuestsCommand(bookingId, body.Guests), ct)).ToHttp())
            .Produces<CancellationResult>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithSummary("Partial cancellation: fewer guests, refund for the removed seats");

        api.MapGet("/me/bookings", async (IQueryHandler<ListMyBookingsQuery, IReadOnlyList<BookingResponse>> h, CancellationToken ct) =>
                (await h.Handle(new ListMyBookingsQuery(), ct)).ToHttp())
            .RequireAuthorization()
            .WithTags("Bookings")
            .Produces<IReadOnlyList<BookingResponse>>()
            .WithSummary("Your bookings, newest departure first");
    }

    private static void MapAgencyBookings(RouteGroupBuilder api)
    {
        var agency = api.MapGroup("/agencies/{agencyId:guid}/bookings").WithTags("Agency: bookings").RequireAuthorization();

        agency.MapGet("/", async (Guid agencyId, DateOnly from, DateOnly to,
            IQueryHandler<ListAgencyBookingsQuery, IReadOnlyList<AgencyBookingResponse>> h, CancellationToken ct) =>
                (await h.Handle(new ListAgencyBookingsQuery(agencyId, from, to), ct)).ToHttp())
            .Produces<IReadOnlyList<AgencyBookingResponse>>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Bookings on the agency's departures between two dates (max 120 days)");

        agency.MapPost("/{bookingId:guid}/cancel", async (Guid agencyId, Guid bookingId, AgencyCancelRequest body,
            ICommandHandler<AgencyCancelBookingCommand, CancellationResult> h, CancellationToken ct) =>
                (await h.Handle(new AgencyCancelBookingCommand(agencyId, bookingId, body.Reason), ct)).ToHttp())
            .Produces<CancellationResult>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithSummary("Cancel a customer's booking; the customer gets a full refund");
    }

    internal sealed record CreateBookingRequest(Guid DepartureId, BookingType Type, int Guests);

    internal sealed record CancelRequest(string? Reason);

    internal sealed record ReduceGuestsRequest(int Guests);

    internal sealed record AgencyCancelRequest(string Reason);
}
