using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly.CircuitBreaker;
using TourDock.Application.Abstractions.Authentication;

namespace TourDock.Infrastructure.Identity;

public sealed class PasswordScreenOptions
{
    public const string SectionName = "Passwords";

    /// <summary>Check new passwords against the Pwned Passwords list. Off means only the length rule applies.</summary>
    public bool CheckBreached { get; init; } = true;

    [Required]
    [Url]
    public string BreachListUrl { get; init; } = "https://api.pwnedpasswords.com/";
}

public static class PasswordScreenHttpClient
{
    public const string Name = "pwned-passwords";
}

/// <summary>
/// Pwned Passwords range API with k-anonymity: only the first 5 hex characters of the password's SHA-1 leave the
/// server; the list of matching suffixes comes back and is compared locally. Responses are padded so their size says
/// nothing either. Fails open with a warning (never logs the password or its hash).
/// </summary>
internal sealed partial class PwnedPasswordScreen(
    IHttpClientFactory httpFactory, IOptions<PasswordScreenOptions> options, ILogger<PwnedPasswordScreen> logger) : IPasswordScreen
{
    public async Task<bool> IsBreachedAsync(string password, CancellationToken cancellationToken)
    {
        if (!options.Value.CheckBreached || string.IsNullOrEmpty(password))
        {
            return false;
        }

        var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(password))); // upper case, what the API uses
        var (prefix, suffix) = (hash[..5], hash[5..]);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"range/{prefix}");
            request.Headers.Add("Add-Padding", "true");
            using var response = await httpFactory.CreateClient(PasswordScreenHttpClient.Name).SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            return Matches(body, suffix);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or BrokenCircuitException && !cancellationToken.IsCancellationRequested)
        {
            LogUnavailable(ex.GetType().Name);
            return false;
        }
    }

    /// <summary>Lines are "SUFFIX:COUNT"; padding lines have a count of 0.</summary>
    internal static bool Matches(string body, string suffix)
    {
        foreach (var line in body.AsSpan().EnumerateLines())
        {
            var colon = line.IndexOf(':');
            if (colon == 35 && line[..colon].Equals(suffix, StringComparison.OrdinalIgnoreCase)
                && int.TryParse(line[(colon + 1)..].Trim(), out var count))
            {
                return count > 0;
            }
        }

        return false;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Breached-password check unavailable ({Reason}); accepting the password on length alone")]
    private partial void LogUnavailable(string reason);
}
