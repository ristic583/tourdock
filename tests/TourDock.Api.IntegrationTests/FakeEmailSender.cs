using System.Collections.Concurrent;
using TourDock.Application.Abstractions;

namespace TourDock.Api.IntegrationTests;

/// <summary>Records every email the API would send.</summary>
public sealed class FakeEmailSender : IEmailSender
{
    public ConcurrentQueue<EmailMessage> Sent { get; } = new();

    public IReadOnlyList<EmailMessage> To(string address) => Sent.Where(m => m.ToAddress == address).ToList();

    public Task<EmailDelivery> SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        Sent.Enqueue(message);
        return Task.FromResult(EmailDelivery.Sent);
    }
}
