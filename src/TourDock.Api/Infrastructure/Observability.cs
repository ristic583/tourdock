using System.Diagnostics;
using Microsoft.AspNetCore.HttpLogging;
using Npgsql;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using TourDock.Application.Observability;

namespace TourDock.Api.Infrastructure;

/// <summary>
/// Logs, traces and metrics. Logs are JSON lines on stdout in containers (one object per line, with trace and span ids);
/// OpenTelemetry export is switched on only when OTEL_EXPORTER_OTLP_ENDPOINT is set, e.g. to the Aspire Dashboard.
/// </summary>
internal static class Observability
{
    public static WebApplicationBuilder AddObservability(this WebApplicationBuilder builder)
    {
        builder.Logging.Configure(o => o.ActivityTrackingOptions = ActivityTrackingOptions.TraceId | ActivityTrackingOptions.SpanId);
        if (!builder.Environment.IsDevelopment())
        {
            builder.Logging.ClearProviders();
            builder.Logging.AddJsonConsole(o =>
            {
                o.IncludeScopes = true; // carries TraceId/SpanId
                o.UseUtcTimestamp = true;
                o.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ ";
            });
        }

        // One line per request: what was asked, the answer and how long it took. No headers, query strings or bodies:
        // those can carry tokens and personal data.
        builder.Services.AddHttpLogging(o =>
        {
            o.LoggingFields = HttpLoggingFields.RequestMethod
                | HttpLoggingFields.RequestPath
                | HttpLoggingFields.ResponseStatusCode
                | HttpLoggingFields.Duration;
            o.CombineLogs = true;
        });

        var exportTo = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
        var otel = builder.Services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService("tourdock-api"))
            .WithTracing(t => t
                .AddAspNetCoreInstrumentation(o => o.Filter = context => !context.Request.Path.StartsWithSegments("/health"))
                .AddHttpClientInstrumentation()
                .AddNpgsql())
            .WithMetrics(m => m
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddMeter(TourDockMetrics.MeterName));

        if (!string.IsNullOrWhiteSpace(exportTo))
        {
            builder.Logging.AddOpenTelemetry(o =>
            {
                o.IncludeScopes = true;
                o.IncludeFormattedMessage = true;
            });
            otel.UseOtlpExporter();
        }

        return builder;
    }

    /// <summary>The W3C trace id of the current request, the same value as TraceId in the logs.</summary>
    public static string? CurrentTraceId(HttpContext context) => Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;
}
