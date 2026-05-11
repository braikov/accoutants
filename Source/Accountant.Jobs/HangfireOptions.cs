namespace Accountant.Jobs;

public sealed class HangfireOptions
{
    public const string SectionName = "Hangfire";

    /// Connection string for the Hangfire schema. When unset, the host's
    /// primary connection string is reused.
    public string? ConnectionString { get; set; }

    /// Path under which the dashboard is mounted. Admin-only.
    public string DashboardPath { get; set; } = "/Administration/Hangfire";
}
