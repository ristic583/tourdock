namespace TourDock.Application.Abstractions;

/// <summary>The authenticated caller of the current request.</summary>
public interface IUserContext
{
    bool IsAuthenticated { get; }

    /// <summary>Throws if the caller is not authenticated. Use only behind an authorization requirement.</summary>
    Guid UserId { get; }

    bool IsAdmin { get; }
}
