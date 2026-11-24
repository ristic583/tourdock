using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpLogging;
using Scalar.AspNetCore;
using TourDock.Api.Endpoints;
using TourDock.Api.Infrastructure;
using TourDock.Application;
using TourDock.Infrastructure;
using TourDock.Infrastructure.OAuth;

var builder = WebApplication.CreateBuilder(args);

builder.AddObservability();
builder.AddHardening();
builder.Services.AddOpenApi();
builder.Services.AddTourDockMcp(builder.Configuration);

// Enums travel as readable strings ("Shared", "Boat"), never as numbers.
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
// Every error carries the trace id that's also on the request's log lines, so a user can quote it.
builder.Services.AddProblemDetails(o => o.CustomizeProblemDetails = context =>
    context.ProblemDetails.Extensions["traceId"] = Observability.CurrentTraceId(context.HttpContext));
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

app.UseForwardedHeaders(); // first: everything after it sees the visitor's IP
app.UseSecurityHeaders();
app.UseHttpLogging();
app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(); // interactive API docs at /scalar
}

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter(); // after authentication: per-user limits need to know the user
app.UseRequestTimeouts();

// Liveness: the process is up and serving requests. No dependency checks,
// so a database outage does not cause the orchestrator to restart the API.
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false })
    .DisableHttpMetrics()
    .WithHttpLogging(HttpLoggingFields.None); // probed every few seconds: not worth a log line each time

// Readiness: the instance can do useful work (database reachable). A slow or stuck outbox reports Degraded,
// which still answers 200: late emails are worth a look, not a reason to take the API out of rotation.
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains(HealthCheckTags.Ready),
}).DisableHttpMetrics().WithHttpLogging(HttpLoggingFields.None);

var api = app.MapGroup("/api/v1");
api.MapAuthEndpoints();
api.MapAgencyEndpoints();
api.MapAgencyManagementEndpoints();
api.MapCatalogEndpoints();
api.MapBookingEndpoints();
api.MapPaymentEndpoints();
api.MapPhotoEndpoints();
api.MapTravelInfoEndpoints();
api.MapReviewEndpoints();
api.MapAssistantEndpoints();
app.MapTourDockMcp();
app.MapOAuth();

await app.RunAsync();

