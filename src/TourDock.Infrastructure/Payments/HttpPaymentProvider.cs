using System.Globalization;
using System.Net.Http.Json;
using TourDock.Application.Abstractions.Payments;
using TourDock.Domain.Common;

namespace TourDock.Infrastructure.Payments;

/// <summary>
/// Typed HTTP client for the payment provider. Retries (resilience handler) are safe for these POSTs because every
/// request carries an Idempotency-Key: the provider returns the original result instead of charging twice.
/// </summary>
internal sealed class HttpPaymentProvider(HttpClient http) : IPaymentProvider
{
    public async Task<ProviderPayment> CreatePaymentAsync(Guid paymentId, Money amount, string description, Uri? returnUrl, CancellationToken cancellationToken)
    {
        var response = await SendAsync<CreatedPayment>("v1/payments", paymentId,
            new { reference = paymentId, amount = amount.Amount, currency = amount.Currency, description, returnUrl = returnUrl?.AbsoluteUri },
            cancellationToken);
        return new ProviderPayment(response.Id, response.CheckoutUrl);
    }

    public async Task<ProviderRefund> CreateRefundAsync(
        Guid refundId, string idempotencyKey, string providerPaymentId, Money amount, CancellationToken cancellationToken)
    {
        var response = await SendAsync<CreatedRefund>("v1/refunds", idempotencyKey,
            new { reference = refundId, paymentId = providerPaymentId, amount = amount.Amount, currency = amount.Currency }, cancellationToken);
        return new ProviderRefund(response.Id);
    }

    private Task<T> SendAsync<T>(string path, Guid idempotencyKey, object body, CancellationToken cancellationToken) =>
        SendAsync<T>(path, idempotencyKey.ToString("N", CultureInfo.InvariantCulture), body, cancellationToken);

    private async Task<T> SendAsync<T>(string path, string idempotencyKey, object body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", idempotencyKey);

        try
        {
            using var response = await http.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<T>(cancellationToken)
                ?? throw new PaymentProviderUnavailableException("Empty response from the payment provider.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException
                                   && !cancellationToken.IsCancellationRequested)
        {
            throw new PaymentProviderUnavailableException("The payment provider is unavailable.", ex);
        }
    }

    private sealed record CreatedPayment(string Id, string CheckoutUrl);

    private sealed record CreatedRefund(string Id);
}
