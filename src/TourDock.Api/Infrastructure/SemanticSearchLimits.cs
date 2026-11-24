using System.Threading.RateLimiting;
using TourDock.Application.Abstractions;

namespace TourDock.Api.Infrastructure;

/// <summary>
/// Each new search vector costs Voyage tokens, so every visitor (by IP, as the edge and web app report it) gets a share
/// per minute and per day. Over it, search still answers, in its normal order. Searches made by the assistant and by MCP
/// clients run inside the caller's request and count against the same share. Cached texts are free and not counted.
/// </summary>
internal sealed class PerClientSemanticSearchLimiter : ISemanticSearchLimiter, IDisposable
{
    private readonly IHttpContextAccessor _http;
    private readonly PartitionedRateLimiter<string> _limiter;

    public PerClientSemanticSearchLimiter(IHttpContextAccessor http, IConfiguration config)
    {
        _http = http;
        _limiter = PartitionedRateLimiter.CreateChained(
            PerClient(config.GetValue("RateLimiting:SemanticSearchPerMinute", 20), TimeSpan.FromMinutes(1)),
            PerClient(config.GetValue("RateLimiting:SemanticSearchPerDay", 200), TimeSpan.FromDays(1)));
    }

    public bool TryTake()
    {
        if (_http.HttpContext is not { } context)
        {
            return true;
        }

        using var lease = _limiter.AttemptAcquire(Hardening.ClientIp(context));
        return lease.IsAcquired;
    }

    public void Dispose() => _limiter.Dispose();

    private static PartitionedRateLimiter<string> PerClient(int permits, TimeSpan window) =>
        PartitionedRateLimiter.Create<string, string>(ip =>
            RateLimitPartition.GetFixedWindowLimiter(ip, _ => Hardening.Window(permits, window)));
}
