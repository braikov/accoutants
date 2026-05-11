namespace Accountant.Web.Areas.App.Services;

/// Cookie name + options for the active-tenant cookie. Centralized so the
/// middleware and the `TenantsController.Switch` endpoint stay in sync.
public static class ActiveTenantCookie
{
    public const string Name = ".Accountant.ActiveTenant";

    public static CookieOptions DefaultOptions() => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Lax,
        IsEssential = true,
        Expires = DateTimeOffset.UtcNow.AddDays(30),
        Path = "/",
    };
}
