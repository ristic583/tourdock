using TourDock.Api.Infrastructure;
using TourDock.Application.Abstractions.Messaging;
using TourDock.Application.Agencies;
using TourDock.Application.Catalog;
using TourDock.Domain.Agencies;
using TourDock.Infrastructure;

namespace TourDock.Api.Endpoints;

internal static class AgencyEndpoints
{
    public static RouteGroupBuilder MapAgencyEndpoints(this RouteGroupBuilder api)
    {
        var agencies = api.MapGroup("/agencies").WithTags("Agencies").RequireAuthorization();

        agencies.MapPost("/", async (RegisterAgencyCommand command, ICommandHandler<RegisterAgencyCommand, AgencyResponse> handler,
            CancellationToken ct) =>
                (await handler.Handle(command, ct)).ToHttp(agency => TypedResults.Created($"/api/v1/agencies/{agency.Id}", agency)))
            .WithSummary("Register an agency; you become its manager. Requires admin approval.");

        api.MapGet("/me/agencies", async (IQueryHandler<GetMyAgenciesQuery, IReadOnlyList<AgencyResponse>> handler, CancellationToken ct) =>
                (await handler.Handle(new GetMyAgenciesQuery(), ct)).ToHttp())
            .RequireAuthorization()
            .WithTags("Agencies")
            .WithSummary("Agencies you work for");

        var admin = api.MapGroup("/admin/agencies").WithTags("Admin").RequireAuthorization(AuthPolicies.Admin);

        admin.MapGet("/", async (AgencyStatus? status, int? page, int? pageSize,
            IQueryHandler<ListAgenciesQuery, PagedResponse<AdminAgencyResponse>> handler, CancellationToken ct) =>
                (await handler.Handle(new ListAgenciesQuery(status, page ?? 1, pageSize ?? 20), ct)).ToHttp())
            .WithSummary("Agencies, newest first, optionally filtered by status (e.g. PendingApproval)");

        admin.MapPost("/{agencyId:guid}/approve", async (Guid agencyId, ICommandHandler<ApproveAgencyCommand, AgencyResponse> handler,
            CancellationToken ct) =>
                (await handler.Handle(new ApproveAgencyCommand(agencyId), ct)).ToHttp())
            .WithSummary("Approve a pending agency");

        admin.MapPost("/{agencyId:guid}/suspend", async (Guid agencyId, SuspendRequest body,
            ICommandHandler<SuspendAgencyCommand, AgencyResponse> handler, CancellationToken ct) =>
                (await handler.Handle(new SuspendAgencyCommand(agencyId, body.Reason), ct)).ToHttp())
            .WithSummary("Suspend an agency");

        return api;
    }

    internal sealed record SuspendRequest(string Reason);
}
