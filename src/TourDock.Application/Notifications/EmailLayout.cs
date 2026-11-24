using System.Net;
using System.Text;

namespace TourDock.Application.Notifications;

/// <summary>Colour of the status chip at the top of an email.</summary>
public enum EmailTone
{
    Positive,
    Neutral,
    Warning,
    Negative,
}

/// <summary>Everything one branded email shows, top to bottom. Plain strings: the layout HTML-encodes them.</summary>
public sealed record EmailLayoutModel(string Preheader, string Greeting, string Intro)
{
    /// <summary>Small chip above the greeting, e.g. "Confirmed".</summary>
    public (string Text, EmailTone Tone)? Badge { get; init; }

    public string? Extra { get; init; }

    /// <summary>Card with a title and label/value rows, e.g. the booking.</summary>
    public string? CardTitle { get; init; }

    public IReadOnlyList<(string Label, string Value)> Facts { get; init; } = [];

    public (string Label, string Url)? Button { get; init; }

    /// <summary>Small print under the button, e.g. how long a link stays valid.</summary>
    public string? Note { get; init; }

    /// <summary>Why the recipient gets this email; shown in the footer.</summary>
    public string Reason { get; init; } = "You're receiving this email because you have a TourDock account.";
}

/// <summary>
/// The HTML shell shared by every TourDock email: brand bar, status chip, text, a details card and one button, in the
/// site's colours. Built for email clients rather than browsers: nested tables, inline styles, a 600px column, a
/// bulletproof button, and no images or web fonts, so nothing depends on remote content being allowed.
/// </summary>
public static class EmailLayout
{
    private const string Ink = "#0f1a20";
    private const string Muted = "#5b6b73";
    private const string Line = "#dce3e0";
    private const string Page = "#eef2f0";
    private const string Brand = "#0a6e6a";
    private const string Font = "-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif";

    public static string Render(EmailLayoutModel m)
    {
        var html = new StringBuilder();
        html.Append("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\">")
            .Append("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">")
            .Append("<meta name=\"color-scheme\" content=\"light\"><meta name=\"supported-color-schemes\" content=\"light\">")
            .Append("<title>TourDock</title></head>")
            .Append($"<body style=\"margin:0;padding:0;background:{Page};\">")
            // Preview text in the inbox list; hidden in the message itself.
            .Append("<div style=\"display:none;max-height:0;overflow:hidden;opacity:0;\">").Append(E(m.Preheader)).Append("</div>")
            .Append($"<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"background:{Page};\"><tr><td align=\"center\" style=\"padding:24px 12px;\">")
            .Append($"<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"max-width:600px;font-family:{Font};color:{Ink};\">");

        // Brand bar.
        html.Append($"<tr><td style=\"background:{Brand};border-radius:18px 18px 0 0;padding:18px 28px;\">")
            .Append("<span style=\"font-size:22px;font-weight:700;color:#ffffff;letter-spacing:-0.3px;\">&#9973;&nbsp;TourDock</span>")
            .Append("<span style=\"font-size:13px;color:#cfe9e6;padding-left:10px;\">Tours by local captains &amp; guides</span>")
            .Append("</td></tr>");

        // Body.
        html.Append($"<tr><td style=\"background:#ffffff;padding:28px;border:1px solid {Line};border-top:0;border-radius:0 0 18px 18px;\">");
        if (m.Badge is { } badge)
        {
            var (bg, fg) = Colors(badge.Tone);
            html.Append($"<span style=\"display:inline-block;background:{bg};color:{fg};font-size:12px;font-weight:700;padding:4px 10px;border-radius:999px;text-transform:uppercase;letter-spacing:0.4px;\">")
                .Append(E(badge.Text)).Append("</span>");
        }

        html.Append($"<p style=\"margin:18px 0 6px;font-size:18px;font-weight:700;\">").Append(E(m.Greeting)).Append("</p>")
            .Append("<p style=\"margin:0 0 14px;font-size:15px;line-height:1.55;\">").Append(E(m.Intro)).Append("</p>");
        if (m.Extra is not null)
        {
            html.Append($"<p style=\"margin:0 0 14px;font-size:15px;line-height:1.55;color:{Muted};\">").Append(E(m.Extra)).Append("</p>");
        }

        if (m.Facts.Count > 0)
        {
            html.Append($"<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"margin:8px 0 22px;background:#f6f8f7;border:1px solid {Line};border-radius:14px;\">")
                .Append("<tr><td style=\"padding:16px 18px;\">");
            if (m.CardTitle is not null)
            {
                html.Append("<p style=\"margin:0 0 10px;font-size:17px;font-weight:700;\">").Append(E(m.CardTitle)).Append("</p>");
            }

            html.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\">");
            foreach (var (label, value) in m.Facts)
            {
                html.Append($"<tr><td valign=\"top\" style=\"padding:5px 12px 5px 0;font-size:13px;color:{Muted};white-space:nowrap;width:110px;\">").Append(E(label))
                    .Append("</td><td style=\"padding:5px 0;font-size:14px;font-weight:600;\">").Append(E(value)).Append("</td></tr>");
            }

            html.Append("</table></td></tr></table>");
        }

        if (m.Button is { } button)
        {
            // Bulletproof button: the link itself carries the padding and colour, so it works without CSS support for <td> backgrounds.
            html.Append("<table role=\"presentation\" cellpadding=\"0\" cellspacing=\"0\" style=\"margin:6px 0 10px;\"><tr>")
                .Append($"<td style=\"background:{Brand};border-radius:12px;\">")
                .Append("<a href=\"").Append(E(button.Url)).Append($"\" style=\"display:inline-block;padding:13px 24px;font-size:15px;font-weight:700;color:#ffffff;text-decoration:none;border-radius:12px;font-family:{Font};\">")
                .Append(E(button.Label)).Append("</a></td></tr></table>")
                .Append($"<p style=\"margin:0 0 6px;font-size:12px;color:{Muted};line-height:1.5;\">Button not working? Paste this link into your browser:<br>")
                .Append($"<a href=\"").Append(E(button.Url)).Append($"\" style=\"color:{Brand};word-break:break-all;\">").Append(E(button.Url)).Append("</a></p>");
        }

        if (m.Note is not null)
        {
            html.Append($"<p style=\"margin:10px 0 0;font-size:13px;color:{Muted};line-height:1.5;\">").Append(E(m.Note)).Append("</p>");
        }

        html.Append("</td></tr>");

        // Footer.
        html.Append($"<tr><td style=\"padding:18px 28px;font-size:12px;line-height:1.5;color:{Muted};text-align:center;\">")
            .Append(E(m.Reason)).Append("<br>TourDock &middot; portfolio demo, all content fictional")
            .Append("</td></tr>");

        html.Append("</table></td></tr></table></body></html>");
        return html.ToString();
    }

    private static (string Background, string Foreground) Colors(EmailTone tone) => tone switch
    {
        EmailTone.Positive => ("#e1f1ee", Brand),
        EmailTone.Warning => ("#fdf3dc", "#8a5a00"),
        EmailTone.Negative => ("#fde8e7", "#b42318"),
        _ => (Page, Muted),
    };

    private static string E(string value) => WebUtility.HtmlEncode(value);
}
