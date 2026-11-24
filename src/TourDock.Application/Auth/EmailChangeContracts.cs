using TourDock.Application.Abstractions;
using TourDock.Application.Abstractions.Data;
using TourDock.Application.Notifications;

namespace TourDock.Application.Auth;

public enum AccountNoticeKind
{
    /// <summary>To the current address: someone asked to move the account to another address.</summary>
    EmailChangeRequested,

    /// <summary>To an address that already has an account: someone tried to switch to it.</summary>
    EmailAlreadyRegistered,

    /// <summary>To an address that already has a confirmed account: someone tried to sign up with it.</summary>
    SignUpWithExistingEmail,
}

public static class EmailChangeEmails
{
    public static EmailContent Link(string displayName, string newEmail, string link)
    {
        const string Intro = "You asked to use this address for your TourDock account. Confirm it, and we'll send your bookings and " +
            "updates here.";
        var note = $"The link works for {AccountEmails.ConfirmationLinkLifetime.TotalHours:0} hours. Until then your old address stays " +
            "in use. If you didn't ask for this, ignore this email.";
        var text = $"Hi {displayName},\n\n{Intro}\n\nConfirm your new email: {link}\n\n{note}\n\nTourDock\n";
        var html = EmailLayout.Render(new EmailLayoutModel("Confirm your new email address.", $"Hi {displayName},", Intro)
        {
            Badge = ("Confirm your new email", EmailTone.Neutral),
            Button = ("Use this email", link),
            Note = note,
            Reason = "You're receiving this email because this address was entered as the new email of a TourDock account.",
        });
        return new EmailContent(newEmail, displayName, "Confirm your new email for TourDock", text, html);
    }

    public static EmailContent Notice(AccountNoticeKind kind, UserContact contact, string? pendingEmail, string? accountLink)
    {
        var (subject, intro, extra) = kind switch
        {
            AccountNoticeKind.EmailChangeRequested => (
                "Your TourDock email is about to change",
                $"Someone signed in to your account asked to change its email to {Mask(pendingEmail)}. It changes once the link sent " +
                    "there is opened.",
                "If this wasn't you, change your password now: your current email stays in use until the new one is confirmed."),
            AccountNoticeKind.SignUpWithExistingEmail => (
                "Someone tried to sign up with your email on TourDock",
                "Someone tried to create a new TourDock account with this address. You already have an account with it, so we didn't " +
                    "create another one.",
                "If that was you, sign in with this address and your existing password. Otherwise you don't need to do anything: your " +
                    "password hasn't changed."),
            _ => (
                "Someone tried to use your email on TourDock",
                "Someone tried to change another TourDock account's email to this address. You already have an account with it, so " +
                    "nothing changed.",
                "If that was you, sign in with this address instead. Otherwise you don't need to do anything."),
        };
        var accountLine = accountLink is null ? string.Empty : $"\nYour account: {accountLink}\n";
        var text = $"Hi {contact.DisplayName},\n\n{intro}\n\n{extra}\n{accountLine}\nTourDock\n";
        var html = EmailLayout.Render(new EmailLayoutModel(intro, $"Hi {contact.DisplayName},", intro)
        {
            Badge = ("Security notice", EmailTone.Warning),
            Extra = extra,
            Button = accountLink is null ? null : ("Open your account", accountLink),
            Reason = "You're receiving this email because it's about the security of your TourDock account.",
        });
        return new EmailContent(contact.Email, contact.DisplayName, subject, text, html);
    }

    public static string ChangeLink(string webBaseUrl, Guid userId, string token) =>
        $"{webBaseUrl.TrimEnd('/')}/verify-email?change=1&user={userId:D}&token={Uri.EscapeDataString(token)}";

    /// <summary>"ana.horvat@example.com" → "a•••@example.com": enough to recognise, not enough to harvest.</summary>
    public static string Mask(string? email)
    {
        if (string.IsNullOrEmpty(email) || email.IndexOf('@') is var at && at < 1)
        {
            return "a new address";
        }

        return $"{email[0]}•••{email[at..]}";
    }
}
