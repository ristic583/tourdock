using TourDock.Domain.Common;

namespace TourDock.Domain.Agencies;

public enum AgencyStatus
{
    PendingApproval,
    Active,
    Suspended,
}

/// <summary>
/// Every member is a manager today: the agency's creator, who has full access. Further roles (e.g. guides with
/// read-only access) come with member management.
/// </summary>
public enum AgencyRole
{
    Manager,
}

/// <summary>A tenant: a company or individual that publishes tours. Guards its membership rules.</summary>
public sealed class Agency : Entity, IAuditable
{
    public const int NameMaxLength = 120;

    private readonly List<AgencyMember> _members = [];

    private Agency(Guid id, string name, string contactEmail)
        : base(id)
    {
        Name = name;
        ContactEmail = contactEmail;
        Status = AgencyStatus.PendingApproval;
    }

    private Agency() { }

    public string Name { get; private set; } = null!;

    public string ContactEmail { get; private set; } = null!;

    public AgencyStatus Status { get; private set; }

    public string? SuspensionReason { get; private set; }

    public IReadOnlyCollection<AgencyMember> Members => _members.AsReadOnly();

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public bool IsActive => Status == AgencyStatus.Active;

    /// <summary>Registers a new agency. The registering user becomes its first manager. Needs admin approval.</summary>
    public static Result<Agency> Register(string name, string contactEmail, Guid ownerUserId)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > NameMaxLength)
        {
            return AgencyErrors.InvalidName;
        }

        var agency = new Agency(NewId(), name.Trim(), contactEmail.Trim());
        agency._members.Add(new AgencyMember(agency.Id, ownerUserId, AgencyRole.Manager));
        return agency;
    }

    public Result Approve()
    {
        if (Status != AgencyStatus.PendingApproval)
        {
            return AgencyErrors.InvalidStatusTransition(Status, AgencyStatus.Active);
        }

        Status = AgencyStatus.Active;
        Raise(new AgencyApproved(Id));
        return Result.Success();
    }

    public Result Suspend(string reason)
    {
        if (Status == AgencyStatus.Suspended)
        {
            return AgencyErrors.InvalidStatusTransition(Status, AgencyStatus.Suspended);
        }

        Status = AgencyStatus.Suspended;
        SuspensionReason = reason;
        Raise(new AgencySuspended(Id));
        return Result.Success();
    }

    public Result Reactivate()
    {
        if (Status != AgencyStatus.Suspended)
        {
            return AgencyErrors.InvalidStatusTransition(Status, AgencyStatus.Active);
        }

        Status = AgencyStatus.Active;
        SuspensionReason = null;
        Raise(new AgencyReactivated(Id));
        return Result.Success();
    }

    public bool HasMember(Guid userId) => _members.Any(m => m.UserId == userId);

    public bool IsManager(Guid userId) => _members.Any(m => m.UserId == userId && m.Role == AgencyRole.Manager);
}

/// <summary>Links a user to the agency they work for, with their role there.</summary>
public sealed class AgencyMember
{
    internal AgencyMember(Guid agencyId, Guid userId, AgencyRole role)
    {
        AgencyId = agencyId;
        UserId = userId;
        Role = role;
    }

    private AgencyMember() { }

    public Guid AgencyId { get; private set; }

    public Guid UserId { get; private set; }

    public AgencyRole Role { get; private set; }

}

public static class AgencyErrors
{
    public static readonly Error InvalidName =
        Error.Validation("Agency.InvalidName", $"Name is required and must be at most {Agency.NameMaxLength} characters.");

    public static readonly Error NotActive = Error.Forbidden("Agency.NotActive", "The agency is not active.");

    public static readonly Error NotApproved =
        Error.Conflict("Agency.NotApproved", "Tours can be published once a TourDock admin approves the agency.");

    public static Error InvalidStatusTransition(AgencyStatus from, AgencyStatus to) =>
        Error.Conflict("Agency.InvalidStatusTransition", $"Cannot change agency status from {from} to {to}.");
}
