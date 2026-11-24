using System.Net;
using System.Text;

namespace TourDock.Api.IntegrationTests.TravelInfo;

/// <summary>
/// Replaces the network for an HTTP client: every request goes to <see cref="Respond"/>. Sits below the resilience
/// pipeline, so retries, timeouts and the circuit breaker run for real.
/// </summary>
public sealed class StubHttpHandler : HttpMessageHandler
{
    private int _calls;

    public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Respond { get; set; } =
        (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

    public int Calls => Volatile.Read(ref _calls);

    public List<Uri> Requests { get; } = [];

    public void ReturnJson(string json) =>
        Respond = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });

    public void ReturnText(string text) =>
        Respond = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(text, Encoding.UTF8, "text/plain") });

    public void ReturnStatus(HttpStatusCode status) => Respond = (_, _) => Task.FromResult(new HttpResponseMessage(status));

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _calls);
        lock (Requests)
        {
            Requests.Add(request.RequestUri!);
        }

        return Respond(request, cancellationToken);
    }
}
