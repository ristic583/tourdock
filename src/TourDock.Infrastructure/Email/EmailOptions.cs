using System.ComponentModel.DataAnnotations;

namespace TourDock.Infrastructure.Email;

/// <summary>
/// SMTP settings. Locally: the Mailpit container (nothing leaves the machine). On the LAN deployment: the mail relay,
/// which delivers for real.
/// </summary>
public sealed class EmailOptions
{
    public const string SectionName = "Email";

    [Required]
    public string Host { get; init; } = string.Empty;

    [Range(1, 65535)]
    public int Port { get; init; } = 25;

    /// <summary>Upgrade to TLS with STARTTLS (required when talking to a provider directly; the LAN relay doesn't need it).</summary>
    public bool UseStartTls { get; init; }

    /// <summary>Optional SMTP login. Leave empty for a relay that trusts the network.</summary>
    public string? Username { get; init; }

    public string? Password { get; init; }

    [Required]
    [EmailAddress]
    public string FromAddress { get; init; } = string.Empty;

    [Required]
    public string FromName { get; init; } = "TourDock";

    /// <summary>
    /// Don't send to reserved demo domains (example.com, *.test, *.invalid, ...). On for real delivery: the demo data uses
    /// them, and bounces hurt the sending domain's reputation. Off locally, so Mailpit shows every email.
    /// </summary>
    public bool SkipReservedDomains { get; init; }

    /// <summary>Public address of the web app, for links in emails. Optional.</summary>
    [Url]
    public string? WebBaseUrl { get; init; }

    [Range(1, 120)]
    public int TimeoutSeconds { get; init; } = 15;
}
