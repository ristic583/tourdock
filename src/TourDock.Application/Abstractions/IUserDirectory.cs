namespace TourDock.Application.Abstractions;

public sealed record UserContact(string DisplayName, string Email);

/// <summary>Looks up contact details of users (e.g. customers on an agency's booking list).</summary>
public interface IUserDirectory
{
    Task<IReadOnlyDictionary<Guid, UserContact>> GetContactsAsync(IEnumerable<Guid> userIds, CancellationToken cancellationToken);
}
