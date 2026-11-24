using System.Net;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using TourDock.Infrastructure.OAuth;

namespace TourDock.Api.Infrastructure;

/// <summary>Who the client really is (behind the web app), how much it may ask for, and response headers.</summary>
internal static class Hardening
{
    public const string AuthPolicy = "auth";
    public const string WritePolicy = "writes";
    public const string ReviewPolicy = "reviews";
    public const string AccountEmailPolicy = "account-emails";
    public const string AssistantPolicy = "assistant";
    public const string McpPolicy = "mcp";

    public static WebApplicationBuilder AddHardening(this WebApplicationBuilder builder)
    {
        var config = builder.Configuration;

        // Every browser request reaches the API through the web app's server, which passes the visitor's IP in
        // X-Forwarded-For. Only the web container may set it: anyone else could claim any IP and dodge the limits.
        // MCP clients come through the edge, which also says whether the visitor used HTTPS (X-Forwarded-Proto, kept
        // from the proxy in front of it): OAuth discovery and the MCP resource metadata build their URLs from it.
        builder.Services.Configure<ForwardedHeadersOptions>(o =>
        {
            o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            o.ForwardLimit = 1;
            foreach (var proxy in config.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? [])
            {
                o.KnownProxies.Add(IPAddress.Parse(proxy));
            }
        });

        builder.Services.AddHttpContextAccessor();
        builder.Services.AddSingleton<Application.Abstractions.ISemanticSearchLimiter, PerClientSemanticSearchLimiter>();

        builder.Services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.OnRejected = async (context, cancellationToken) =>
            {
                var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter,
                    out var wait) ? (int)Math.Ceiling(wait.TotalSeconds) : 60;
                context.HttpContext.Response.Headers.RetryAfter = retryAfter.ToString(System.Globalization.CultureInfo.InvariantCulture);
                await Results.Problem(
                        statusCode: StatusCodes.Status429TooManyRequests,
                        title: "Too many requests",
                        detail: RetryText(retryAfter),
                        extensions: new Dictionary<string, object?> { ["code"] = "RateLimit.Exceeded" })
                    .ExecuteAsync(context.HttpContext);
            };

            // Everyone, per IP: generous, it only stops floods. Health probes are never limited.
            o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                context.Request.Path.StartsWithSegments("/health")
                    ? RateLimitPartition.GetNoLimiter("health")
                    : RateLimitPartition.GetFixedWindowLimiter(ClientIp(context),
                        _ => Window(config.GetValue("RateLimiting:GlobalPermitPer10Seconds", 100), TimeSpan.FromSeconds(10))));

            // Brute-force protection for credential endpoints, per IP.
            o.AddPolicy(AuthPolicy, context => RateLimitPartition.GetFixedWindowLimiter(
                ClientIp(context), _ => Window(config.GetValue("RateLimiting:AuthPermitPerMinute", 10), TimeSpan.FromMinutes(1))));

            // Writes that cost something (holds seats, starts payments), per user.
            o.AddPolicy(WritePolicy, context => RateLimitPartition.GetFixedWindowLimiter(
                UserOrIp(context), _ => Window(config.GetValue("RateLimiting:WritesPerMinute", 10), TimeSpan.FromMinutes(1))));

            // Emails we send on request (a new confirmation link, an email change), per account: the IP limit alone would
            // let one account flood its own inbox, or someone else's while changing the address.
            o.AddPolicy(AccountEmailPolicy, context => RateLimitPartition.GetFixedWindowLimiter(
                UserOrIp(context), _ => Window(config.GetValue("RateLimiting:AccountEmailsPerHour", 3), TimeSpan.FromHours(1))));

            // Assistant messages cost model tokens: a person chatting sends a few a minute, and no one visitor may use up
            // the day's global cap (Assistant:MaxMessagesPerDay) for everyone else.
            o.AddPolicy(AssistantPolicy, context => RateLimitPartition.Get(UserOrIp(context), _ => RateLimiter.CreateChained(
                new FixedWindowRateLimiter(Window(config.GetValue("RateLimiting:AssistantPerMinute", 6), TimeSpan.FromMinutes(1))),
                new FixedWindowRateLimiter(Window(config.GetValue("RateLimiting:AssistantPerDay", 30), TimeSpan.FromDays(1))))));

            // MCP clients are agents: a conversation makes a handful of tool calls, a loop makes many.
            o.AddPolicy(McpPolicy, context => RateLimitPartition.GetFixedWindowLimiter(
                ClientIp(context), _ => Window(config.GetValue("RateLimiting:McpPerMinute", 30), TimeSpan.FromMinutes(1))));

            // Client registration is anonymous: a client registers once, so a handful an hour per address is plenty.
            o.AddPolicy(OAuthEndpoints.RegistrationRateLimitPolicy, context => RateLimitPartition.GetFixedWindowLimiter(
                ClientIp(context), _ => Window(config.GetValue("RateLimiting:OAuthRegistrationsPerHour", 10), TimeSpan.FromHours(1))));

            // Reviews and reports: nobody needs more than a few a minute.
            o.AddPolicy(ReviewPolicy, context => RateLimitPartition.GetFixedWindowLimiter(
                UserOrIp(context), _ => Window(config.GetValue("RateLimiting:ReviewsPerMinute", 5), TimeSpan.FromMinutes(1))));
        });

        // Bodies are small JSON; photo uploads raise this per endpoint. A stuck request is cut off after 30 s.
        builder.WebHost.ConfigureKestrel(k =>
        {
            k.Limits.MaxRequestBodySize = 1024 * 1024;
            k.AddServerHeader = false; // don't advertise the server software
        });
        builder.Services.AddRequestTimeouts(o => o.DefaultPolicy = new Microsoft.AspNetCore.Http.Timeouts.RequestTimeoutPolicy
        {
            Timeout = TimeSpan.FromSeconds(config.GetValue("RequestTimeoutSeconds", 30)),
            TimeoutStatusCode = StatusCodes.Status504GatewayTimeout,
        });

        return builder;
    }

    /// <summary>
    /// Headers for a JSON API that no browser renders: no sniffing, no framing, no referrer, nothing may load. Responses
    /// to signed-in requests are never cached. The interactive docs (Development only) need their own scripts, so the
    /// CSP skips them.
    /// </summary>
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) => app.Use(async (context, next) =>
    {
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;
            headers.XContentTypeOptions = "nosniff";
            headers.XFrameOptions = "DENY";
            headers["Referrer-Policy"] = "no-referrer";
            if (!context.Request.Path.StartsWithSegments("/scalar") && !context.Request.Path.StartsWithSegments("/openapi"))
            {
                headers.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";
            }

            if (context.Request.Headers.Authorization.Count > 0)
            {
                headers.CacheControl = "no-store";
            }

            return Task.CompletedTask;
        });
        await next();
    });

    internal static FixedWindowRateLimiterOptions Window(int permits, TimeSpan window) => new()
    {
        PermitLimit = permits,
        Window = window,
        QueueLimit = 0,
    };

    /// <summary>Seconds read as minutes or hours: a daily limit shouldn't say "try again in 50400 seconds".</summary>
    internal static string RetryText(int seconds) => seconds switch
    {
        < 120 => $"Please slow down and try again in {seconds} seconds.",
        < 3600 => $"Please slow down and try again in {(int)Math.Ceiling(seconds / 60.0)} minutes.",
        _ => $"You've reached the limit for today. Please try again in about {(int)Math.Ceiling(seconds / 3600.0)} hours.",
    };

    internal static string ClientIp(HttpContext context) => context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    private static string UserOrIp(HttpContext context) =>
        context.User.FindFirstValue("sub") is { } userId ? $"user:{userId}" : $"ip:{ClientIp(context)}";
}
