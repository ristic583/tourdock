using Microsoft.Extensions.Options;
using TourDock.Application.Abstractions.Payments;

namespace TourDock.Infrastructure.Payments;

/// <summary>
/// Allows only absolute http(s) URLs whose origin exactly matches a configured one: no user info, no other ports,
/// no look-alike hosts ("app.example.com.evil.test"), no scheme-relative or javascript: URLs.
/// </summary>
internal sealed class ReturnUrlPolicy(IOptions<PaymentsOptions> options) : IReturnUrlPolicy
{
    private readonly HashSet<string> _origins = options.Value.AllowedReturnOrigins
        .Select(o => Uri.TryCreate(o, UriKind.Absolute, out var uri) ? Origin(uri) : null)
        .OfType<string>()
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    public bool IsAllowed(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
        && string.IsNullOrEmpty(uri.UserInfo)
        && _origins.Contains(Origin(uri));

    private static string Origin(Uri uri) => $"{uri.Scheme}://{uri.IdnHost}:{uri.Port}";
}
