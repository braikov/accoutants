namespace Accountant.Email.Models;

public class PasswordChangedModel
{
    public string Email { get; set; } = string.Empty;
    public DateTime ChangedAtUtc { get; set; }
    public string IpAddress { get; set; } = string.Empty;

    /// Pre-formatted local time string for display in the template.
    public string FormattedChangedAt { get; set; } = string.Empty;
}
