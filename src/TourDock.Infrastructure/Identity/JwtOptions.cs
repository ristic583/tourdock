using System.ComponentModel.DataAnnotations;

namespace TourDock.Infrastructure.Identity;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    [Required]
    public string Issuer { get; init; } = "tourdock";

    [Required]
    public string Audience { get; init; } = "tourdock";

    /// <summary>HMAC-SHA256 key. Comes from configuration (env var Jwt__SigningKey), never from source.</summary>
    [Required]
    [MinLength(32, ErrorMessage = "Jwt:SigningKey must be at least 32 characters.")]
    public string SigningKey { get; init; } = string.Empty;

    [Range(1, 60)]
    public int AccessTokenMinutes { get; init; } = 15;

    [Range(1, 90)]
    public int RefreshTokenDays { get; init; } = 14;
}

public sealed class AdminSeedOptions
{
    public const string SectionName = "Admin";

    /// <summary>When both values are set, an admin account is created on startup if it does not exist yet.</summary>
    public string? Email { get; init; }

    public string? Password { get; init; }
}
