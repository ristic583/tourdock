using Microsoft.EntityFrameworkCore;
using OpenIddict.EntityFrameworkCore.Models;

namespace TourDock.Infrastructure.OAuth;

/// <summary>OpenIddict's tables (apps, authorizations, scopes, tokens) in the same database, named like ours.</summary>
internal static class OAuthModel
{
    public static void Map(ModelBuilder modelBuilder)
    {
        modelBuilder.UseOpenIddict<Guid>();
        modelBuilder.Entity<OpenIddictEntityFrameworkCoreApplication<Guid>>().ToTable("oauth_applications");
        modelBuilder.Entity<OpenIddictEntityFrameworkCoreAuthorization<Guid>>().ToTable("oauth_authorizations");
        modelBuilder.Entity<OpenIddictEntityFrameworkCoreScope<Guid>>().ToTable("oauth_scopes");
        modelBuilder.Entity<OpenIddictEntityFrameworkCoreToken<Guid>>().ToTable("oauth_tokens");
    }
}
