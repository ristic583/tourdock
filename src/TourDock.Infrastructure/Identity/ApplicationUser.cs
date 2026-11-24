using Microsoft.AspNetCore.Identity;

namespace TourDock.Infrastructure.Identity;

public sealed class ApplicationUser : IdentityUser<Guid>
{
    public string DisplayName { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
}

public static class Roles
{
    public const string Admin = "Admin";

    /// <summary>Fixed id so the role can be seeded by a migration.</summary>
    public static readonly Guid AdminRoleId = new("0195f0a0-0000-7000-8000-000000000001");
}

/// <summary>
/// A refresh token. Only a SHA-256 hash is stored, so a database leak does not leak usable tokens.
/// Tokens issued from one login share a <see cref="FamilyId"/>; reuse of a rotated token revokes the family.
/// </summary>
public sealed class RefreshToken
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid UserId { get; set; }

    public Guid FamilyId { get; set; }

    public string TokenHash { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    public string? RevokedReason { get; set; }

    public Guid? ReplacedByTokenId { get; set; }

    public uint Version { get; set; }

    public bool IsActive(DateTimeOffset now) => RevokedAt is null && now < ExpiresAt;
}
