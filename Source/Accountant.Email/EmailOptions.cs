namespace Accountant.Email;

/// Bound from the `Email` configuration section. Single from-account for now;
/// extend to per-purpose accounts (noreply / support / notifications) when there
/// is a real reason to differentiate.
public class EmailOptions
{
    public const string SectionName = "Email";

    /// When false, the SMTP sender logs "would send" and returns without IO.
    /// Useful in Development / AutomatedTest where you don't want real mail going out.
    public bool Enabled { get; set; } = true;

    /// Locale used when callers don't supply one. Templates fall back to
    /// `<TemplateName>.<DefaultCulture>.cshtml` if the requested file is missing.
    public string DefaultCulture { get; set; } = "bg-BG";

    /// Display name + address used as the message From header.
    public EmailAddress From { get; set; } = new();

    /// SMTP transport.
    public SmtpSettings Smtp { get; set; } = new();
}

public class EmailAddress
{
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
}

public class SmtpSettings
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    public bool EnableSsl { get; set; } = true;

    /// Allow self-signed / invalid TLS certificates. ONLY for local dev / smtp4dev.
    public bool AllowInvalidCertificate { get; set; } = false;

    /// One of `None | Auto | SslOnConnect | StartTls | StartTlsWhenAvailable`.
    /// Falls back to `StartTls` when unparseable.
    public string SecureSocketOptions { get; set; } = "StartTls";

    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}
