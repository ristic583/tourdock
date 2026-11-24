namespace TourDock.Application.Abstractions;

/// <summary>One email to one recipient. <paramref name="Key"/> is stable across retries (used as the Message-ID).</summary>
public sealed record EmailMessage(string Key, string ToAddress, string ToName, string Subject, string TextBody, string HtmlBody);

public enum EmailDelivery
{
    /// <summary>Accepted by the mail server.</summary>
    Sent,

    /// <summary>Permanently refused (e.g. no such mailbox). Retrying won't help.</summary>
    Rejected,

    /// <summary>Not sent on purpose, e.g. a reserved demo domain such as example.com.</summary>
    Skipped,
}

public interface IEmailSender
{
    /// <summary>Sends the email. Throws on temporary failures (server down, timeout) so the caller retries later.</summary>
    Task<EmailDelivery> SendAsync(EmailMessage message, CancellationToken cancellationToken);
}

/// <summary>Remembers which notifications were already delivered, so a retried outbox message doesn't email twice.</summary>
public interface ISentNotificationLog
{
    Task<bool> WasSentAsync(string key, CancellationToken cancellationToken);

    /// <summary>Records the delivery; persisted by the next unit-of-work save.</summary>
    void Record(string key, EmailDelivery delivery);
}

/// <summary>Settings the notification texts need.</summary>
/// <param name="WebBaseUrl">Public address of the web app for links in emails, e.g. "https://tourdock.example.com". Null = no links.</param>
public sealed record NotificationSettings(string? WebBaseUrl);
