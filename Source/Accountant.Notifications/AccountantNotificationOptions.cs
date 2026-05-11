namespace Accountant.Notifications;

/// Host-side knobs for notification dispatch. Bound from the
/// `Notifications` configuration section.
public sealed class AccountantNotificationOptions
{
    public const string SectionName = "Notifications";

    /// Default culture used when a recipient profile has no culture set.
    /// Templates fall back to this when a localized template is missing.
    public string DefaultCulture { get; set; } = "bg-BG";
}
