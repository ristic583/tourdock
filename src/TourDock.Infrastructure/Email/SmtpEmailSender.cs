using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using TourDock.Application.Abstractions;

namespace TourDock.Infrastructure.Email;

/// <summary>
/// Sends through SMTP with MailKit (Microsoft recommends it over System.Net.Mail.SmtpClient). One connection per email:
/// volumes are small and the outbox already sends them one by one.
/// </summary>
internal sealed class SmtpEmailSender(IOptions<EmailOptions> options) : IEmailSender
{
    // RFC 2606 / RFC 6761 names that must never receive real mail.
    private static readonly string[] ReservedDomains = ["example.com", "example.net", "example.org"];
    private static readonly string[] ReservedSuffixes = [".test", ".example", ".invalid", ".localhost", ".local"];

    public async Task<EmailDelivery> SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (settings.SkipReservedDomains && IsReserved(message.ToAddress))
        {
            return EmailDelivery.Skipped;
        }

        var mime = new MimeMessage
        {
            Subject = message.Subject,
            // Same id on every retry, so mail clients can drop a duplicate if one ever gets through.
            MessageId = $"{message.Key}@{DomainOf(settings.FromAddress)}",
            Body = new BodyBuilder { TextBody = message.TextBody, HtmlBody = message.HtmlBody }.ToMessageBody(),
        };
        mime.From.Add(new MailboxAddress(settings.FromName, settings.FromAddress));
        mime.To.Add(new MailboxAddress(message.ToName, message.ToAddress));

        using var smtp = new SmtpClient { Timeout = settings.TimeoutSeconds * 1000 };
        try
        {
            await smtp.ConnectAsync(settings.Host, settings.Port, settings.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.None, cancellationToken);
            if (!string.IsNullOrEmpty(settings.Username))
            {
                await smtp.AuthenticateAsync(settings.Username, settings.Password ?? string.Empty, cancellationToken);
            }

            await smtp.SendAsync(new SendRequest(mime), cancellationToken); // MailKit 5: sending takes a request
            await smtp.DisconnectAsync(quit: true, cancellationToken);
            return EmailDelivery.Sent;
        }
        catch (SmtpCommandException ex) when ((int)ex.StatusCode >= 500 && ex.ErrorCode is SmtpErrorCode.RecipientNotAccepted)
        {
            // 5xx for this recipient (no such mailbox, domain doesn't accept mail): retrying won't change the answer.
            return EmailDelivery.Rejected;
        }
    }

    internal static bool IsReserved(string address)
    {
        var domain = DomainOf(address).ToLowerInvariant();
        return ReservedDomains.Any(d => domain == d || domain.EndsWith("." + d, StringComparison.Ordinal))
               || ReservedSuffixes.Any(suffix => domain.EndsWith(suffix, StringComparison.Ordinal))
               || domain is "localhost";
    }

    private static string DomainOf(string address) => address[(address.LastIndexOf('@') + 1)..];
}
