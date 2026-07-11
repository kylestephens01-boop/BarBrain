using System.Security.Claims;
using BarBrain.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace BarBrain.Api.Endpoints;

/// <summary>
/// Sprint 6 write guard: banned accounts can't write, even inside the cookie's
/// security-stamp validation window (ban rotates the stamp, which evicts the
/// session within Identity's validation interval — this filter closes the gap
/// on write surfaces immediately). Read surfaces don't need it: the ban's
/// content is already excluded by the shadow/hidden read filters.
/// </summary>
public static class ModerationGuards
{
    public static async ValueTask<object?> NotBannedFilter(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var idClaim = context.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (idClaim is not null && Guid.TryParse(idClaim, out var userId))
        {
            var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
            var banned = await db.Users.AsNoTracking()
                .AnyAsync(u => u.Id == userId && u.BannedAt != null, context.HttpContext.RequestAborted);
            if (banned)
                return Results.Unauthorized();
        }
        return await next(context);
    }
}
