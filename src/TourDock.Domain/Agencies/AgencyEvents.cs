using TourDock.Domain.Common;

namespace TourDock.Domain.Agencies;

/// <summary>An admin approved the agency: its tours can go on sale.</summary>
public sealed record AgencyApproved(Guid AgencyId) : IDomainEvent;

/// <summary>An admin suspended the agency: its tours are hidden. The reason stays on the agency.</summary>
public sealed record AgencySuspended(Guid AgencyId) : IDomainEvent;

/// <summary>An admin lifted a suspension.</summary>
public sealed record AgencyReactivated(Guid AgencyId) : IDomainEvent;
