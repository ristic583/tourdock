using System.Text.Json;
using TourDock.Api.Infrastructure;
using TourDock.Application.Abstractions.Messaging;
using TourDock.Application.Abstractions.Payments;
using TourDock.Application.Payments;
using TourDock.Infrastructure;

namespace TourDock.Api.Endpoints;

internal static class PaymentEndpoints
{
    public const string SignatureHeader = "TourDock-Signature";

    public static RouteGroupBuilder MapPaymentEndpoints(this RouteGroupBuilder api)
    {
        api.MapPost("/bookings/{bookingId:guid}/payments", async (
                    Guid bookingId, StartPaymentRequest? body, ICommandHandler<StartPaymentCommand, PaymentResponse> h,
                        CancellationToken ct) =>
                (await h.Handle(new StartPaymentCommand(bookingId, body?.ReturnUrl), ct)).ToHttp())
            .RequireAuthorization()
            .WithTags("Payments")
            .Produces<PaymentResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .RequireRateLimiting(Hardening.WritePolicy)
            .WithSummary("Start (or resume) paying a pending booking; returns the provider's checkout URL")
            .WithDescription(
                "Optional body { returnUrl }: an absolute URL on an allow-listed origin (Payments:AllowedReturnOrigins). " +
                "After Pay or Decline the provider redirects the customer there, adding ?payment=succeeded|failed as a hint. " +
                "The hint is not proof: read the booking (status, lastPaymentStatus), which the signed webhook updates.");

        var admin = api.MapGroup("/admin/refunds").WithTags("Admin").RequireAuthorization(AuthPolicies.Admin);
        admin.MapGet("/", async (IQueryHandler<ListFailedRefundsQuery, IReadOnlyList<FailedRefundResponse>> h, CancellationToken ct) =>
                (await h.Handle(new ListFailedRefundsQuery(), ct)).ToHttp())
            .Produces<IReadOnlyList<FailedRefundResponse>>()
            .WithSummary("Refunds the provider declined too often: the customer is still owed the money");
        admin.MapPost("/{refundId:guid}/retry", async (Guid refundId, RetryRefundRequest b, ICommandHandler<RetryRefundCommand, Unit> h,
            CancellationToken ct) =>
                (await h.Handle(new RetryRefundCommand(refundId, b.Note), ct)).ToHttp(_ => TypedResults.NoContent()))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithSummary("Send a failed refund to the provider again (audited)");

        api.MapPost("/webhooks/payments", HandleWebhookAsync)
            .AllowAnonymous()
            .WithTags("Payments")
            .WithSummary("Payment provider events (signed). Not for clients.");

        return api;
    }

    private static async Task<IResult> HandleWebhookAsync(
        HttpRequest request, IWebhookSignatureVerifier verifier, ICommandHandler<HandlePaymentEventCommand, Unit> handler,
            CancellationToken ct)
    {
        // The signature covers the exact bytes sent, so read the raw body before any JSON parsing.
        using var reader = new StreamReader(request.Body);
        var body = await reader.ReadToEndAsync(ct);
        if (!verifier.IsValid(request.Headers[SignatureHeader], body))
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Invalid signature");
        }

        WebhookPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<WebhookPayload>(body, JsonSerializerOptions.Web);
        }
        catch (JsonException)
        {
            payload = null;
        }

        PaymentEventType? type = payload?.Type switch
        {
            "payment.succeeded" => PaymentEventType.PaymentSucceeded,
            "payment.failed" => PaymentEventType.PaymentFailed,
            "refund.succeeded" => PaymentEventType.RefundSucceeded,
            "refund.failed" => PaymentEventType.RefundFailed,
            _ => null,
        };

        if (payload is null || type is null || !Guid.TryParse(payload.Data?.Reference, out var reference))
        {
            // Unknown event types are acknowledged so the provider stops retrying them.
            return TypedResults.Ok();
        }

        var result = await handler.Handle(new HandlePaymentEventCommand(payload.Id, type.Value, reference, payload.Data!.Reason), ct);
        return result.ToHttp(_ => TypedResults.Ok());
    }

    internal sealed record StartPaymentRequest(string? ReturnUrl);

    internal sealed record RetryRefundRequest(string Note);

    private sealed record WebhookPayload(string Id, string Type, WebhookData? Data);

    private sealed record WebhookData(string? Reference, string? Reason);
}
