using System.Globalization;
using System.Security.Claims;
using Accountant.DataAccess.Entities.Product;
using Accountant.Web.Areas.App.Services;

namespace Accountant.Web.Areas.App.Middleware;

/// For authenticated users:
/// 1. Reads the active-tenant cookie.
/// 2. Validates that the user is a member of that tenant; if not, falls back
///    to their first membership.
/// 3. If the user has zero memberships → calls `TenantService.EnsureDefaultTenantAsync`
///    to create the "<email>'s firm" default. This is the auto-bootstrap for
///    freshly-registered users (replaces an explicit hook in `BaseAccountController.Register`).
/// 4. Populates `IActiveTenantAccessor.Current` so controllers / views can
///    filter by tenant.
/// 5. Persists the (possibly corrected) tenant id back to the cookie.
///
/// Runs after `UseAuthentication` + `UseAuthorization`. Anonymous and
/// non-cookie-auth requests pass through untouched.
public sealed class ActiveTenantMiddleware
{
    private readonly RequestDelegate _next;

    public ActiveTenantMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(
        HttpContext context,
        TenantService tenants,
        IActiveTenantAccessor accessor)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        var userIdString = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(userIdString, NumberStyles.Integer, CultureInfo.InvariantCulture, out var userId))
        {
            await _next(context).ConfigureAwait(false);
            return;
        }
        var cancellationToken = context.RequestAborted;

        int? cookieTenantId = null;
        if (context.Request.Cookies.TryGetValue(ActiveTenantCookie.Name, out var raw)
            && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            cookieTenantId = parsed;
        }

        TenantSummary? resolved = null;
        if (cookieTenantId is int candidate
            && await tenants.IsMemberAsync(userId, candidate, cancellationToken).ConfigureAwait(false))
        {
            var name = await ResolveTenantNameAsync(tenants, userId, candidate, cancellationToken).ConfigureAwait(false);
            if (name is not null)
            {
                resolved = new TenantSummary(candidate, name, TenantRole.Member);
            }
        }

        if (resolved is null)
        {
            var email = context.User.FindFirstValue(ClaimTypes.Email) ?? context.User.Identity!.Name ?? "user";
            resolved = await tenants.EnsureDefaultTenantAsync(userId, email, cancellationToken).ConfigureAwait(false);
        }

        ((ActiveTenantAccessor)accessor).Current = new ActiveTenant(resolved.Id, resolved.Name);

        if (cookieTenantId != resolved.Id)
        {
            context.Response.Cookies.Append(
                ActiveTenantCookie.Name,
                resolved.Id.ToString(CultureInfo.InvariantCulture),
                ActiveTenantCookie.DefaultOptions());
        }

        await _next(context).ConfigureAwait(false);
    }

    private static async Task<string?> ResolveTenantNameAsync(
        TenantService tenants,
        int userId,
        int tenantId,
        CancellationToken cancellationToken)
    {
        var memberships = await tenants.ListMembershipsAsync(userId, cancellationToken).ConfigureAwait(false);
        return memberships.FirstOrDefault(m => m.Id == tenantId)?.Name;
    }
}
