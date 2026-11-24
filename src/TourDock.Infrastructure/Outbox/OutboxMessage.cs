using System.Text.Json;
using TourDock.Application.Abstractions.Data;

namespace TourDock.Infrastructure.Outbox;

public sealed class OutboxMessage
{
    public static OutboxMessage Create(IOutboxMessage message, DateTimeOffset now) => new()
    {
        Type = message.GetType().FullName!,
        Payload = JsonSerializer.Serialize(message, message.GetType()),
        OccurredAt = now,
        NextAttemptAt = now,
    };

    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>CLR type name of the message (resolved through a registry, never by loading arbitrary types).</summary>
    public string Type { get; set; } = string.Empty;

    public string Payload { get; set; } = string.Empty;

    public DateTimeOffset OccurredAt { get; set; }

    public DateTimeOffset NextAttemptAt { get; set; }

    public DateTimeOffset? ProcessedAt { get; set; }

    public int Attempts { get; set; }

    public string? LastError { get; set; }
}

/// <summary>Provider event ids that were already applied (webhook de-duplication).</summary>
public sealed class ProcessedEvent
{
    public string Source { get; set; } = string.Empty;

    public string EventId { get; set; } = string.Empty;

    public DateTimeOffset ProcessedAt { get; set; }
}

/// <summary>Notification emails already handed to the mail server (de-duplication for at-least-once delivery).</summary>
public sealed class SentNotification
{
    public string Key { get; set; } = string.Empty;

    public string Delivery { get; set; } = string.Empty;

    public DateTimeOffset SentAt { get; set; }
}
