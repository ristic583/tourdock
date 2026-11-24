using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using TourDock.Application.Abstractions.Authentication;
using TourDock.Domain.Common;
using TourDock.Infrastructure.Persistence;

namespace TourDock.Infrastructure.Identity;

internal sealed partial class TokenService(
    TourDockDbContext db,
    IOptions<JwtOptions> options,
    TimeProvider timeProvider,
    ILogger<TokenService> logger) : ITokenService
{
    private readonly JwtOptions _options = options.Value;

    public async Task<AuthTokens> IssueAsync(AuthenticatedUser user, CancellationToken cancellationToken)
    {
        var tokens = CreateTokens(user, familyId: Guid.CreateVersion7(), out var refreshToken);
        db.RefreshTokens.Add(refreshToken);
        await db.SaveChangesAsync(cancellationToken);
        return tokens;
    }

    public async Task<Result<AuthTokens>> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var stored = await db.RefreshTokens.SingleOrDefaultAsync(t => t.TokenHash == Hash(refreshToken), cancellationToken);
        if (stored is null)
        {
            return AuthErrors.InvalidRefreshToken;
        }

        if (stored.RevokedAt is not null)
        {
            // A rotated (already used) token was presented again: it was probably stolen. Kill the whole session.
            LogReuseDetected(logger, stored.UserId, stored.FamilyId);
            await RevokeFamilyAsync(stored.FamilyId, "Reuse detected", now, cancellationToken);
            return AuthErrors.InvalidRefreshToken;
        }

        if (!stored.IsActive(now))
        {
            return AuthErrors.InvalidRefreshToken;
        }

        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == stored.UserId, cancellationToken);
        if (user is null || (user.LockoutEnd is { } lockoutEnd && lockoutEnd > now))
        {
            return AuthErrors.InvalidRefreshToken;
        }

        var roles = await db.UserRoles.Where(ur => ur.UserId == user.Id)
            .Join(db.Roles, ur => ur.RoleId, r => r.Id, (_, r) => r.Name!)
            .ToListAsync(cancellationToken);

        var tokens = CreateTokens(new AuthenticatedUser(user.Id, user.Email!, user.DisplayName, roles, user.EmailConfirmed), stored.FamilyId, out var next);
        stored.RevokedAt = now;
        stored.RevokedReason = "Rotated";
        stored.ReplacedByTokenId = next.Id;
        db.RefreshTokens.Add(next);

        // If two requests rotate the same token concurrently, the row version makes one of them fail.
        await db.SaveChangesAsync(cancellationToken);
        return tokens;
    }

    public async Task RevokeAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var stored = await db.RefreshTokens.AsNoTracking().SingleOrDefaultAsync(t => t.TokenHash == Hash(refreshToken), cancellationToken);
        if (stored is not null)
        {
            await RevokeFamilyAsync(stored.FamilyId, "Logout", timeProvider.GetUtcNow(), cancellationToken);
        }
    }

    private Task RevokeFamilyAsync(Guid familyId, string reason, DateTimeOffset now, CancellationToken cancellationToken) =>
        db.RefreshTokens
            .Where(t => t.FamilyId == familyId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now).SetProperty(t => t.RevokedReason, reason), cancellationToken);

    private AuthTokens CreateTokens(AuthenticatedUser user, Guid familyId, out RefreshToken refreshToken)
    {
        var now = timeProvider.GetUtcNow();
        var accessExpires = now.AddMinutes(_options.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(JwtRegisteredClaimNames.Name, user.DisplayName),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };
        claims.AddRange(user.Roles.Select(role => new Claim("role", role)));

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            Subject = new ClaimsIdentity(claims),
            NotBefore = now.UtcDateTime,
            IssuedAt = now.UtcDateTime,
            Expires = accessExpires.UtcDateTime,
            SigningCredentials = new SigningCredentials(SigningKey(_options), SecurityAlgorithms.HmacSha256),
        };
        var accessToken = new JwtSecurityTokenHandler().CreateEncodedJwt(descriptor);

        var rawRefresh = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        refreshToken = new RefreshToken
        {
            UserId = user.Id,
            FamilyId = familyId,
            TokenHash = Hash(rawRefresh),
            CreatedAt = now,
            ExpiresAt = now.AddDays(_options.RefreshTokenDays),
        };

        return new AuthTokens(accessToken, accessExpires, rawRefresh, refreshToken.ExpiresAt);
    }

    internal static SymmetricSecurityKey SigningKey(JwtOptions options) => new(Encoding.UTF8.GetBytes(options.SigningKey));

    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    [LoggerMessage(Level = LogLevel.Warning, Message = "Refresh token reuse detected for user {UserId}; revoked token family {FamilyId}")]
    private static partial void LogReuseDetected(ILogger logger, Guid userId, Guid familyId);
}
