namespace Accountant.Email.Models;

public class PasswordResetModel
{
    public string Email { get; set; } = string.Empty;
    public string CallbackUrl { get; set; } = string.Empty;
    public string? ShortCode { get; set; }
}
