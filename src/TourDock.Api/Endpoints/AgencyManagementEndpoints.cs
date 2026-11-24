using TourDock.Api.Infrastructure;
using TourDock.Application.Abstractions.Messaging;
using TourDock.Application.Agencies;
using TourDock.Application.Slots;
using TourDock.Application.Tours;
using TourDock.Application.Vehicles;
using TourDock.Domain.Tours;
using TourDock.Domain.Vehicles;

namespace TourDock.Api.Endpoints;

/// <summary>Agency back office: vehicles, tours, pricing, schedules and departures. All routes are scoped to one agency.</summary>
internal static class AgencyManagementEndpoints
{
    public static RouteGroupBuilder MapAgencyManagementEndpoints(this RouteGroupBuilder api)
    {
        var agency = api.MapGroup("/agencies/{agencyId:guid}").RequireAuthorization();
        MapVehicles(agency.MapGroup("/vehicles").WithTags("Agency: vehicles"));
        MapTours(agency.MapGroup("/tours").WithTags("Agency: tours"));
        MapSlots(agency.MapGroup("/slots").WithTags("Agency: departures"));

        agency.MapGet("/stats", async (Guid agencyId, DateOnly from, DateOnly to,
            IQueryHandler<GetAgencyStatsQuery, AgencyStatsResponse> h, CancellationToken ct) =>
                (await h.Handle(new GetAgencyStatsQuery(agencyId, from, to), ct)).ToHttp())
            .WithTags("Agency: stats")
            .Produces<AgencyStatsResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Overview KPIs: departures, seats, revenue net of refunds for departures between two local dates (max 366 " +
                "days), today, holds")
            .WithDescription("Members of the agency only (admins read-only). Non-members get 404.");
        return api;
    }

    private static void MapVehicles(RouteGroupBuilder vehicles)
    {
        vehicles.MapGet("/", async (Guid agencyId, IQueryHandler<ListVehiclesQuery, IReadOnlyList<VehicleResponse>> h,
            CancellationToken ct) =>
            (await h.Handle(new ListVehiclesQuery(agencyId), ct)).ToHttp());

        vehicles.MapPost("/", async (Guid agencyId, VehicleRequest body, ICommandHandler<CreateVehicleCommand, VehicleResponse> h,
            CancellationToken ct) =>
            (await h.Handle(new CreateVehicleCommand(agencyId, body.Name, body.Type, body.Capacity), ct))
                .ToHttp(v => TypedResults.Created($"/api/v1/agencies/{agencyId}/vehicles/{v.Id}", v)));

        vehicles.MapPut("/{vehicleId:guid}", async (Guid agencyId, Guid vehicleId, VehicleRequest body,
            ICommandHandler<UpdateVehicleCommand, VehicleResponse> h, CancellationToken ct) =>
            (await h.Handle(new UpdateVehicleCommand(agencyId, vehicleId, body.Name, body.Type, body.Capacity), ct)).ToHttp());

        vehicles.MapDelete("/{vehicleId:guid}", async (Guid agencyId, Guid vehicleId, ICommandHandler<DeleteVehicleCommand, Unit> h,
            CancellationToken ct) =>
            (await h.Handle(new DeleteVehicleCommand(agencyId, vehicleId), ct)).ToHttp(_ => TypedResults.NoContent()));
    }

    private static void MapTours(RouteGroupBuilder tours)
    {
        tours.MapGet("/", async (Guid agencyId, IQueryHandler<ListAgencyToursQuery, IReadOnlyList<TourResponse>> h, CancellationToken ct) =>
            (await h.Handle(new ListAgencyToursQuery(agencyId), ct)).ToHttp());

        tours.MapGet("/{tourId:guid}", async (Guid agencyId, Guid tourId, IQueryHandler<GetAgencyTourQuery, TourResponse> h,
            CancellationToken ct) =>
            (await h.Handle(new GetAgencyTourQuery(agencyId, tourId), ct)).ToHttp());

        tours.MapPost("/", async (Guid agencyId, CreateTourRequest b, ICommandHandler<CreateTourCommand, TourResponse> h,
            CancellationToken ct) =>
                (await h.Handle(new CreateTourCommand(agencyId, b.Title, b.Description, b.Location, b.DurationMinutes, b.MaxGuests,
                    b.BookingMode, b.PricePerPerson, b.PrivatePrice, b.CancellationPolicy), ct))
                .ToHttp(t => TypedResults.Created($"/api/v1/agencies/{agencyId}/tours/{t.Id}", t)))
            .WithSummary("Create a tour (starts as a draft)");

        tours.MapPut("/{tourId:guid}", async (Guid agencyId, Guid tourId, TourDetailsRequest b,
            ICommandHandler<UpdateTourDetailsCommand, TourResponse> h, CancellationToken ct) =>
            (await h.Handle(new UpdateTourDetailsCommand(agencyId, tourId, b.Title, b.Description, b.Location, b.DurationMinutes,
                b.MaxGuests), ct)).ToHttp());

        tours.MapPut("/{tourId:guid}/pricing", async (Guid agencyId, Guid tourId, PricingRequest b,
            ICommandHandler<UpdateTourPricingCommand, TourResponse> h, CancellationToken ct) =>
                (await h.Handle(new UpdateTourPricingCommand(agencyId, tourId, b.BookingMode, b.PricePerPerson, b.PrivatePrice),
                    ct)).ToHttp())
            .WithSummary("Change booking mode and base prices (existing bookings keep their price)");

        tours.MapPut("/{tourId:guid}/cancellation-policy", async (Guid agencyId, Guid tourId, IReadOnlyList<RefundTierDto> tiers,
            ICommandHandler<ChangeCancellationPolicyCommand, TourResponse> h, CancellationToken ct) =>
            (await h.Handle(new ChangeCancellationPolicyCommand(agencyId, tourId, tiers), ct)).ToHttp());

        tours.MapPost("/{tourId:guid}/publish", async (Guid agencyId, Guid tourId, ICommandHandler<PublishTourCommand, TourResponse> h,
            CancellationToken ct) =>
            (await h.Handle(new PublishTourCommand(agencyId, tourId), ct)).ToHttp());

        tours.MapPost("/{tourId:guid}/unpublish", async (Guid agencyId, Guid tourId, ICommandHandler<UnpublishTourCommand, TourResponse> h,
            CancellationToken ct) =>
            (await h.Handle(new UnpublishTourCommand(agencyId, tourId), ct)).ToHttp());

        tours.MapDelete("/{tourId:guid}", async (Guid agencyId, Guid tourId, ICommandHandler<DeleteTourCommand, Unit> h,
            CancellationToken ct) =>
            (await h.Handle(new DeleteTourCommand(agencyId, tourId), ct)).ToHttp(_ => TypedResults.NoContent()));

        tours.MapPost("/{tourId:guid}/seasons", async (Guid agencyId, Guid tourId, SeasonRequest b,
            ICommandHandler<AddSeasonalPriceCommand, TourResponse> h, CancellationToken ct) =>
            (await h.Handle(new AddSeasonalPriceCommand(agencyId, tourId, b.From, b.To, b.PricePerPerson, b.PrivatePrice), ct)).ToHttp());

        tours.MapDelete("/{tourId:guid}/seasons/{seasonId:guid}", async (Guid agencyId, Guid tourId, Guid seasonId,
            ICommandHandler<RemoveSeasonalPriceCommand, TourResponse> h, CancellationToken ct) =>
            (await h.Handle(new RemoveSeasonalPriceCommand(agencyId, tourId, seasonId), ct)).ToHttp());

        tours.MapPost("/{tourId:guid}/schedule-rules", async (Guid agencyId, Guid tourId, ScheduleRuleRequest b,
            ICommandHandler<AddScheduleRuleCommand, ScheduleRuleCreatedResponse> h, CancellationToken ct) =>
                (await h.Handle(new AddScheduleRuleCommand(agencyId, tourId, b.VehicleId, b.Days, b.StartTime, b.ValidFrom, b.ValidUntil),
                    ct))
                .ToHttp(r => TypedResults.Created($"/api/v1/agencies/{agencyId}/tours/{tourId}", r)))
            .WithSummary("Add a recurring schedule; departures for the next 365 days are created immediately");

        tours.MapDelete("/{tourId:guid}/schedule-rules/{ruleId:guid}", async (Guid agencyId, Guid tourId, Guid ruleId,
            ICommandHandler<DeactivateScheduleRuleCommand, TourResponse> h, CancellationToken ct) =>
                (await h.Handle(new DeactivateScheduleRuleCommand(agencyId, tourId, ruleId), ct)).ToHttp())
            .WithSummary("Stop a schedule (existing departures and bookings are kept)");

        tours.MapPost("/{tourId:guid}/slots", async (Guid agencyId, Guid tourId, CreateSlotRequest b,
            ICommandHandler<CreateSlotCommand, SlotResponse> h, CancellationToken ct) =>
                (await h.Handle(new CreateSlotCommand(agencyId, tourId, b.VehicleId, b.LocalStart), ct))
                .ToHttp(s => TypedResults.Created($"/api/v1/agencies/{agencyId}/slots/{s.Id}", s)))
            .WithSummary("Add a one-off departure (start time in the tour's local time)");
    }

    private static void MapSlots(RouteGroupBuilder slots)
    {
        slots.MapGet("/", async (Guid agencyId, DateOnly from, DateOnly to, Guid? tourId,
            IQueryHandler<ListSlotsQuery, IReadOnlyList<SlotResponse>> h, CancellationToken ct) =>
                (await h.Handle(new ListSlotsQuery(agencyId, from, to, tourId), ct)).ToHttp())
            .WithSummary("Departures between two local dates (max 120 days)");

        slots.MapPost("/{slotId:guid}/block", async (Guid agencyId, Guid slotId, ICommandHandler<BlockSlotCommand, SlotResponse> h,
            CancellationToken ct) =>
            (await h.Handle(new BlockSlotCommand(agencyId, slotId), ct)).ToHttp());

        slots.MapPost("/{slotId:guid}/unblock", async (Guid agencyId, Guid slotId, ICommandHandler<UnblockSlotCommand, SlotResponse> h,
            CancellationToken ct) =>
            (await h.Handle(new UnblockSlotCommand(agencyId, slotId), ct)).ToHttp());

        slots.MapPost("/{slotId:guid}/cancel", async (Guid agencyId, Guid slotId, CancelSlotRequest b,
            ICommandHandler<CancelSlotCommand, SlotResponse> h, CancellationToken ct) =>
                (await h.Handle(new CancelSlotCommand(agencyId, slotId, b.Reason), ct)).ToHttp())
            .WithSummary("Cancel a departure; all its bookings are cancelled with a full refund");
    }

    internal sealed record VehicleRequest(string Name, VehicleType Type, int Capacity);

    internal sealed record CreateTourRequest(
        string Title, string Description, LocationDto Location, int DurationMinutes, int MaxGuests, BookingMode BookingMode,
        decimal? PricePerPerson, decimal? PrivatePrice, IReadOnlyList<RefundTierDto>? CancellationPolicy);

    internal sealed record TourDetailsRequest(string Title, string Description, LocationDto Location, int DurationMinutes, int MaxGuests);

    internal sealed record PricingRequest(BookingMode BookingMode, decimal? PricePerPerson, decimal? PrivatePrice);

    internal sealed record SeasonRequest(DateOnly From, DateOnly To, decimal? PricePerPerson, decimal? PrivatePrice);

    internal sealed record ScheduleRuleRequest(Guid VehicleId, DaysOfWeek Days, TimeOnly StartTime, DateOnly ValidFrom,
        DateOnly? ValidUntil);

    internal sealed record CreateSlotRequest(Guid VehicleId, DateTime LocalStart);

    internal sealed record CancelSlotRequest(string Reason);
}
