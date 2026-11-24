using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace TourDock.Infrastructure.Identity;

/// <summary>Creates the admin account from configuration (Admin__Email / Admin__Password) if it does not exist.</summary>
internal sealed partial class AdminSeeder(IServiceScopeFactory scopes, IOptions<AdminSeedOptions> options, ILogger<AdminSeeder> logger)
    : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var (email, password) = (options.Value.Email, options.Value.Password);
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            return;
        }

        await using var scope = scopes.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        if (await users.FindByEmailAsync(email) is not null)
        {
            return;
        }

        var admin = new ApplicationUser
        {
            Id = Guid.CreateVersion7(),
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            DisplayName = "Administrator",
            CreatedAt = scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow(),
        };

        var created = await users.CreateAsync(admin, password);
        if (created.Succeeded)
        {
            await users.AddToRoleAsync(admin, Roles.Admin);
            LogSeeded(logger);
        }
        else
        {
            // Error codes only: Identity's descriptions can repeat the email address, which doesn't belong in logs.
            LogFailed(logger, string.Join(", ", created.Errors.Select(e => e.Code)));
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(Level = LogLevel.Information, Message = "Admin account created from configuration")]
    private static partial void LogSeeded(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Could not create the admin account: {ErrorCodes}")]
    private static partial void LogFailed(ILogger logger, string errorCodes);
}
