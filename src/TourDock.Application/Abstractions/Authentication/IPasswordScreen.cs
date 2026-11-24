namespace TourDock.Application.Abstractions.Authentication;

/// <summary>Checks a new password against passwords known from data breaches.</summary>
public interface IPasswordScreen
{
    /// <summary>
    /// True when the password is known to be breached. Fails open (false) when the check can't run, so an outage of the
    /// breach list never blocks sign-ups; the length rule still applies.
    /// </summary>
    Task<bool> IsBreachedAsync(string password, CancellationToken cancellationToken);
}
