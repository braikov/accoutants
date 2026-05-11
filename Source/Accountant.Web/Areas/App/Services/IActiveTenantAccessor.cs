namespace Accountant.Web.Areas.App.Services;

/// Scoped accessor exposing the active tenant for the current request.
/// Populated by `ActiveTenantMiddleware` (after authentication). Returns
/// `null` for anonymous requests or for users with zero memberships.
public interface IActiveTenantAccessor
{
    ActiveTenant? Current { get; }
}

public sealed class ActiveTenantAccessor : IActiveTenantAccessor
{
    public ActiveTenant? Current { get; set; }
}
