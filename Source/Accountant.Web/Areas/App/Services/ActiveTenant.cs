namespace Accountant.Web.Areas.App.Services;

/// Snapshot of the user's active tenant for the current request. Populated
/// by `ActiveTenantMiddleware`; read by controllers and views via
/// `IActiveTenantAccessor`.
public sealed record ActiveTenant(int Id, string Name);
