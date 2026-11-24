using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using TourDock.Application.Abstractions.Authentication;

namespace TourDock.Infrastructure.Identity;

/// <summary>
/// Failed sign-ins per (email, client address) in a sliding window, kept in memory (per instance; several instances
/// would share it through a distributed cache). Five failures throttle that address for that email for 15 minutes;
/// the owner signing in from elsewhere is unaffected. The email is stored only as a hash.
/// </summary>
internal sealed class LoginThrottle(IMemoryCache cache, IHttpContextAccessor http) : ILoginThrottle
{
    public const int MaxFailures = 5;

    public static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    private readonly Lock _gate = new();

    public bool IsThrottled(string email) => cache.TryGetValue(Key(email), out int failures) && failures >= MaxFailures;

    public void RecordFailure(string email)
    {
        var key = Key(email);
        lock (_gate)
        {
            var failures = cache.TryGetValue(key, out int count) ? count : 0;
            cache.Set(key, failures + 1, new MemoryCacheEntryOptions { SlidingExpiration = Window });
        }
    }

    public void Reset(string email) => cache.Remove(Key(email));

    private string Key(string email)
    {
        var address = http.HttpContext?.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(email.Trim().ToUpperInvariant())));
        return $"login-failures:{hash}:{address}";
    }
}
