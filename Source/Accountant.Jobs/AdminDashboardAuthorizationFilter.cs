using Hangfire.Annotations;
using Hangfire.Dashboard;

namespace Accountant.Jobs;

/// Restricts the Hangfire dashboard to authenticated users in the `Admin` role.
/// The dashboard exposes operational data (job arguments, payloads, retry
/// counts) — anonymous access would leak document IDs and tenant info.
public sealed class AdminDashboardAuthorizationFilter : IDashboardAuthorizationFilter
{
    public bool Authorize([NotNull] DashboardContext context)
    {
        var httpContext = context.GetHttpContext();
        return httpContext.User.Identity?.IsAuthenticated == true
            && httpContext.User.IsInRole("Admin");
    }
}
