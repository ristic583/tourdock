using TourDock.Domain.Common;

namespace TourDock.Domain.Administration;

/// <summary>
/// One entry in the admin audit log: who did what to which record, why, and when. Append-only: entries are never
/// changed or deleted by the application.
/// </summary>
public sealed class AdminAction : Entity
{
    public const int ReasonMaxLength = 500;

    private AdminAction(Guid id)
        : base(id) { }

    private AdminAction() { }

    public Guid AdminId { get; private set; }

    /// <summary>Stable action name, e.g. "Agency.Approve", "Review.Hide".</summary>
    public string Action { get; private set; } = null!;

    public string TargetType { get; private set; } = null!;

    public Guid TargetId { get; private set; }

    public string? Reason { get; private set; }

    public DateTimeOffset At { get; private set; }

    public static AdminAction Record(Guid adminId, string action, string targetType, Guid targetId, string? reason, DateTimeOffset now) => new(NewId())
    {
        AdminId = adminId,
        Action = action,
        TargetType = targetType,
        TargetId = targetId,
        Reason = reason is { Length: > ReasonMaxLength } ? reason[..ReasonMaxLength] : reason,
        At = now,
    };
}
