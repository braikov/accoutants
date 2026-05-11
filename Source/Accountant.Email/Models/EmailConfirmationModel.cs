namespace Accountant.Email.Models;

public class EmailConfirmationModel
{
    public string Email { get; set; } = string.Empty;
    public string CallbackUrl { get; set; } = string.Empty;

    /// 6-digit code from Braikov.Identity.ShortCodes when installed; null
    /// otherwise. Templates render a code block only when non-null.
    public string? ShortCode { get; set; }
}
