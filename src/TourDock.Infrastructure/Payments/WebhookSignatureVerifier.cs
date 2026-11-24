using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using TourDock.Application.Abstractions.Payments;

namespace TourDock.Infrastructure.Payments;

/// <summary>
/// Header format: "t={unix seconds},v1={hex HMAC-SHA256 of "{t}.{raw body}"}" (same scheme as major providers).
/// Rejects wrong signatures and old timestamps (replays), and compares in constant time.
/// </summary>
internal sealed class WebhookSignatureVerifier(IOptions<PaymentsOptions> options, TimeProvider time) : IWebhookSignatureVerifier
{
    public bool IsValid(string? signatureHeader, string body)
    {
        if (string.IsNullOrEmpty(signatureHeader))
        {
            return false;
        }

        var pairs = signatureHeader.Split(',', StringSplitOptions.TrimEntries)
            .Select(p => p.Split('=', 2))
            .Where(p => p.Length == 2)
            .ToList();

        // A repeated key ("t=1,t=2") is ambiguous: refuse it rather than guess which one was signed.
        if (pairs.GroupBy(p => p[0], StringComparer.Ordinal).Any(g => g.Count() > 1))
        {
            return false;
        }

        var parts = pairs.ToDictionary(p => p[0], p => p[1], StringComparer.Ordinal);

        if (!parts.TryGetValue("t", out var timestamp) || !parts.TryGetValue("v1", out var signature)
            || !long.TryParse(timestamp, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds))
        {
            return false;
        }

        var age = time.GetUtcNow() - DateTimeOffset.FromUnixTimeSeconds(seconds);
        if (age.Duration() > TimeSpan.FromSeconds(options.Value.WebhookToleranceSeconds))
        {
            return false;
        }

        var expected = Sign(options.Value.WebhookSecret, timestamp, body);
        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(signature.ToLowerInvariant()));
    }

    public static string Sign(string secret, string timestamp, string body) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{timestamp}.{body}")));
}
