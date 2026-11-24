using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

// Payment sandbox: behaves like a hosted payment provider (API key, idempotency keys, hosted checkout page,
// asynchronous signed webhooks with retries). State is in memory; restarting the container clears it.
var builder = WebApplication.CreateBuilder(args);
var settings = builder.Configuration.GetSection("Sandbox").Get<SandboxSettings>() ?? new SandboxSettings();
if (settings.ApiKey.Length < 16 || settings.WebhookSecret.Length < 32 || string.IsNullOrWhiteSpace(settings.WebhookUrl))
{
    throw new InvalidOperationException("Configure Sandbox__ApiKey (16+ chars), Sandbox__WebhookSecret (32+ chars) and Sandbox__WebhookUrl.");
}

builder.Services.AddSingleton(settings);
builder.Services.AddSingleton<SandboxStore>();
builder.Services.AddSingleton<Wallets>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(Channel.CreateUnbounded<WebhookEvent>());
builder.Services.AddHttpClient("webhooks", c => c.Timeout = TimeSpan.FromSeconds(10));
builder.Services.AddHostedService<WebhookSender>();
builder.Services.AddHealthChecks();

var app = builder.Build();
app.MapHealthChecks("/health");

var api = app.MapGroup("/v1").AddEndpointFilter(async (context, next) =>
{
    var auth = context.HttpContext.Request.Headers.Authorization.ToString();
    return auth == $"Bearer {settings.ApiKey}" ? await next(context) : Results.Unauthorized();
});

api.MapPost("/payments", (HttpRequest request, CreatePayment body, SandboxStore store) =>
{
    if (request.Headers["Idempotency-Key"].ToString() is not { Length: > 0 } key)
    {
        return Results.BadRequest(new { error = "Idempotency-Key header is required" });
    }

    Uri? returnUrl = null;
    if (body.ReturnUrl is not null
        && (!Uri.TryCreate(body.ReturnUrl, UriKind.Absolute, out returnUrl) || (returnUrl.Scheme != Uri.UriSchemeHttps && returnUrl.Scheme != Uri.UriSchemeHttp)))
    {
        return Results.BadRequest(new { error = "returnUrl must be an absolute http(s) URL" });
    }

    // Like real providers, the return URL is fixed when the payment is created; a replayed request keeps the first one.
    var payment = store.Payments.GetOrAdd(store.PaymentKeys.GetOrAdd(key, _ => $"pay_{Guid.NewGuid():N}"),
        id => new SandboxPayment(id, body.Reference, body.Amount, body.Currency, body.Description, returnUrl));
    return Results.Ok(new { id = payment.Id, status = payment.Status, checkoutUrl = $"{settings.PublicBaseUrl.TrimEnd('/')}/checkout/{payment.Id}" });
});

api.MapPost("/payments/{id}/simulate", async (string id, Simulate body, SandboxStore store, Channel<WebhookEvent> webhooks) =>
    store.Payments.TryGetValue(id, out var payment)
        ? Results.Ok(new { id, status = await CompleteAsync(payment, body.Outcome == "succeed" ? null : "Card declined (sandbox)", webhooks) })
        : Results.NotFound());

api.MapPost("/refunds", async (HttpRequest request, CreateRefund body, SandboxStore store, Channel<WebhookEvent> webhooks) =>
{
    if (request.Headers["Idempotency-Key"].ToString() is not { Length: > 0 } key)
    {
        return Results.BadRequest(new { error = "Idempotency-Key header is required" });
    }

    if (!store.Payments.TryGetValue(body.PaymentId, out var payment) || payment.Status != "succeeded")
    {
        return Results.UnprocessableEntity(new { error = "Payment not found or not succeeded" });
    }

    var isNew = false;
    var refundId = store.RefundKeys.GetOrAdd(key, _ =>
    {
        isNew = true;
        return $"re_{Guid.NewGuid():N}";
    });

    if (isNew)
    {
        // Real providers settle refunds asynchronously; the result arrives later as a webhook.
        await webhooks.Writer.WriteAsync(new WebhookEvent("refund.succeeded", body.Reference, refundId, body.Amount, body.Currency, null, TimeSpan.FromSeconds(1)));
    }

    return Results.Ok(new { id = refundId, status = "pending" });
});

// Hosted checkout page (what the customer would see on the provider's site). Once the payment is decided, the same
// address shows the 3-D Secure step: "confirming with your bank", then approved or declined, then back to the merchant.
// It stays on /checkout/{id} so a reverse proxy that forwards only /checkout/{id} and /checkout/{id}/complete covers it.
// The payment is already decided (and its webhook queued) when that step shows, so it only tells the customer the
// outcome and sends them back. The status in the return URL is only a hint; the merchant trusts the signed webhook.
app.MapGet("/checkout/{id}", (string id, HttpContext http, SandboxStore store, Wallets wallets) =>
{
    if (!store.Payments.TryGetValue(id, out var payment))
    {
        return Results.NotFound();
    }

    if (payment.Status == "requires_payment")
    {
        return Results.Content(CheckoutPage.Render(payment, wallets.Get(WalletId(http)), new CheckoutForm()), "text/html");
    }

    return payment.ReturnUrl is { } returnUrl
        ? Results.Content(CheckoutPage.Authenticate(payment, WithQuery(returnUrl, "payment", payment.Status)), "text/html")
        : Results.Content(CheckoutPage.Result(payment.Status), "text/html");
});

app.MapPost("/checkout/{id}/complete", async (string id, HttpContext http, SandboxStore store, Wallets wallets, TimeProvider time, Channel<WebhookEvent> webhooks) =>
{
    if (!store.Payments.TryGetValue(id, out var payment))
    {
        return Results.NotFound();
    }

    var form = await http.Request.ReadFormAsync();
    var walletId = WalletId(http);
    string status;
    if (form["action"] == "cancel")
    {
        // Back to the merchant without paying; the payment stays open, so the customer can try again.
        status = "cancelled";
    }
    else if (form["card"] == "new")
    {
        var (card, errors) = TestCards.Validate(form["number"], form["expiry"], form["cvc"], form["holder"], time.GetUtcNow());
        if (card is null)
        {
            var again = new CheckoutForm
            {
                Method = "new",
                Number = form["number"],
                Expiry = form["expiry"],
                Holder = form["holder"],
                Save = form["save"] == "on",
                Errors = errors,
            };
            return Results.Content(CheckoutPage.Render(payment, wallets.Get(walletId), again), "text/html", statusCode: StatusCodes.Status422UnprocessableEntity);
        }

        if (form["save"] == "on")
        {
            wallets.Add(walletId, card);
        }

        status = await CompleteAsync(payment, card.DeclineReason, webhooks);
    }
    else if (wallets.Get(walletId).FirstOrDefault(c => c.Id == form["card"]) is { } saved)
    {
        status = await CompleteAsync(payment, saved.DeclineReason, webhooks);
    }
    else
    {
        var none = new CheckoutForm { Errors = new Dictionary<string, string> { ["card"] = "Choose a card." } };
        return Results.Content(CheckoutPage.Render(payment, wallets.Get(walletId), none), "text/html", statusCode: StatusCodes.Status422UnprocessableEntity);
    }

    if (payment.ReturnUrl is null)
    {
        return Results.Content(CheckoutPage.Result(status), "text/html");
    }

    // 303 See Other: the browser follows with a GET, so a refresh can't re-submit the form. A cancel goes straight back;
    // a card payment shows the bank confirmation step first.
    http.Response.Headers.Location = status == "cancelled"
        ? WithQuery(payment.ReturnUrl, "payment", status)
        : $"/checkout/{Uri.EscapeDataString(payment.Id)}";
    return Results.StatusCode(StatusCodes.Status303SeeOther);
}).DisableAntiforgery();

app.Run();

/// <summary>Completes the payment once (approved when <paramref name="declineReason"/> is null) and queues the webhook.</summary>
static async Task<string> CompleteAsync(SandboxPayment payment, string? declineReason, Channel<WebhookEvent> webhooks)
{
    var succeed = declineReason is null;
    if (payment.TryComplete(succeed ? "succeeded" : "failed"))
    {
        await webhooks.Writer.WriteAsync(new WebhookEvent(
            succeed ? "payment.succeeded" : "payment.failed", payment.Reference, payment.Id, payment.Amount, payment.Currency, declineReason, TimeSpan.Zero));
    }

    return payment.Status;
}

/// <summary>The browser's sandbox wallet (saved cards), from its cookie; a new one is started when there is none.</summary>
static string WalletId(HttpContext http)
{
    if (http.Request.Cookies[Wallets.CookieName] is { Length: 32 } existing && existing.All(char.IsAsciiHexDigitLower))
    {
        return existing;
    }

    var id = Guid.NewGuid().ToString("N");
    http.Response.Cookies.Append(Wallets.CookieName, id, new CookieOptions
    {
        HttpOnly = true,
        SameSite = SameSiteMode.Lax,
        Secure = http.Request.IsHttps,
        MaxAge = TimeSpan.FromDays(30),
    });
    return id;
}

static string WithQuery(Uri url, string name, string value)
{
    var builder = new UriBuilder(url);
    var pair = $"{Uri.EscapeDataString(name)}={Uri.EscapeDataString(value)}";
    builder.Query = string.IsNullOrEmpty(builder.Query) || builder.Query == "?" ? pair : $"{builder.Query.TrimStart('?')}&{pair}";
    return builder.Uri.AbsoluteUri;
}

internal sealed record SandboxSettings
{
    public string ApiKey { get; init; } = string.Empty;

    public string WebhookSecret { get; init; } = string.Empty;

    public string WebhookUrl { get; init; } = string.Empty;

    public string PublicBaseUrl { get; init; } = "http://localhost:8090";
}

internal sealed record CreatePayment(string Reference, decimal Amount, string Currency, string Description, string? ReturnUrl);

internal sealed record CreateRefund(string Reference, string PaymentId, decimal Amount, string Currency);

internal sealed record Simulate(string Outcome);

internal sealed record WebhookEvent(string Type, string Reference, string ObjectId, decimal Amount, string Currency, string? Reason, TimeSpan Delay);

internal sealed class SandboxPayment(string id, string reference, decimal amount, string currency, string description, Uri? returnUrl)
{
    private readonly Lock _lock = new();

    public string Id { get; } = id;

    public string Reference { get; } = reference;

    public decimal Amount { get; } = amount;

    public string Currency { get; } = currency;

    public string Description { get; } = description;

    public Uri? ReturnUrl { get; } = returnUrl;

    public string Status { get; private set; } = "requires_payment";

    public bool TryComplete(string status)
    {
        lock (_lock)
        {
            if (Status != "requires_payment")
            {
                return false;
            }

            Status = status;
            return true;
        }
    }
}

internal sealed class SandboxStore
{
    public ConcurrentDictionary<string, SandboxPayment> Payments { get; } = new();

    public ConcurrentDictionary<string, string> PaymentKeys { get; } = new();

    public ConcurrentDictionary<string, string> RefundKeys { get; } = new();
}

/// <summary>Delivers webhooks at least once, signed like a real provider, retrying with backoff when the receiver fails.</summary>
internal sealed partial class WebhookSender(
    Channel<WebhookEvent> channel, IHttpClientFactory httpFactory, SandboxSettings settings, ILogger<WebhookSender> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var webhook in channel.Reader.ReadAllAsync(stoppingToken))
        {
            _ = DeliverAsync(webhook, stoppingToken);
        }
    }

    private async Task DeliverAsync(WebhookEvent webhook, CancellationToken stoppingToken)
    {
        await Task.Delay(webhook.Delay, stoppingToken);
        var body = JsonSerializer.Serialize(new
        {
            id = $"evt_{Guid.NewGuid():N}",
            type = webhook.Type,
            created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            data = new { reference = webhook.Reference, objectId = webhook.ObjectId, amount = webhook.Amount, currency = webhook.Currency, reason = webhook.Reason },
        });

        for (var attempt = 1; attempt <= 6 && !stoppingToken.IsCancellationRequested; attempt++)
        {
            try
            {
                var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
                var signature = Convert.ToHexStringLower(HMACSHA256.HashData(
                    Encoding.UTF8.GetBytes(settings.WebhookSecret), Encoding.UTF8.GetBytes($"{timestamp}.{body}")));
                using var request = new HttpRequestMessage(HttpMethod.Post, settings.WebhookUrl)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json"),
                };
                request.Headers.Add("TourDock-Signature", $"t={timestamp},v1={signature}");

                using var response = await httpFactory.CreateClient("webhooks").SendAsync(request, stoppingToken);
                if (response.IsSuccessStatusCode)
                {
                    LogDelivered(logger, webhook.Type, webhook.Reference);
                    return;
                }

                LogRetry(logger, webhook.Type, (int)response.StatusCode, attempt);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !stoppingToken.IsCancellationRequested)
            {
                LogRetry(logger, webhook.Type, 0, attempt);
            }

            await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), stoppingToken);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Webhook {Type} for {Reference} delivered")]
    private static partial void LogDelivered(ILogger logger, string type, string reference);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Webhook {Type} delivery failed (status {Status}), attempt {Attempt}")]
    private static partial void LogRetry(ILogger logger, string type, int status, int attempt);
}
