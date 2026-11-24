using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using TourDock.Application.Abstractions;

namespace TourDock.Infrastructure.Identity;

internal sealed class UserContext(IHttpContextAccessor accessor) : IUserContext
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public Guid UserId => Guid.TryParse(Principal?.FindFirstValue("sub"), out var id)
        ? id
        : throw new InvalidOperationException("No authenticated user in the current request.");

    public bool IsAdmin => Principal?.IsInRole(Roles.Admin) == true;
}
