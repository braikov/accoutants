using System.Net.Security;
using Accountant.Email.Models;
using Braikov.Identity.Core.Abstractions;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Accountant.Email.Services;

/// MailKit-based SMTP sender. Renders the Razor template, builds a MimeMessage,
/// connects + authenticates + sends. Honors `EmailOptions.Enabled = false` as
/// a global "log-and-skip" switch so dev environments can avoid real IO.
public class SmtpEmailSender : IEmailSender
{
    private readonly EmailOptions _options;
    private readonly IEmailTemplateRenderer _renderer;
    private readonly ILogger<SmtpEmailSender> _log;

    public SmtpEmailSender(
        IOptions<EmailOptions> options,
        IEmailTemplateRenderer renderer,
        ILogger<SmtpEmailSender> log)
    {
        _options = options.Value;
        _renderer = renderer;
        _log = log;
    }

    public async Task SendEmailConfirmationAsync(string email, string callbackUrl, string? shortCode, string? culture, CancellationToken cancellationToken)
    {
        var model = new EmailConfirmationModel { Email = email, CallbackUrl = callbackUrl, ShortCode = shortCode };
        var html = await _renderer.RenderAsync("EmailConfirmation", model, culture, cancellationToken);
        var subject = LocalizeSubject(culture, "Потвърдете акаунта си", "Confirm your account");
        await SendInternalAsync(email, subject, html, cancellationToken);
    }

    public async Task SendPasswordResetAsync(string email, string callbackUrl, string? shortCode, string? culture, CancellationToken cancellationToken)
    {
        var model = new PasswordResetModel { Email = email, CallbackUrl = callbackUrl, ShortCode = shortCode };
        var html = await _renderer.RenderAsync("PasswordReset", model, culture, cancellationToken);
        var subject = LocalizeSubject(culture, "Нулиране на парола", "Password reset");
        await SendInternalAsync(email, subject, html, cancellationToken);
    }

    public async Task SendPasswordChangedAsync(string email, DateTime changedAtUtc, string ipAddress, string? culture, CancellationToken cancellationToken)
    {
        var isBg = !string.Equals(culture, "en-GB", StringComparison.OrdinalIgnoreCase);
        var model = new PasswordChangedModel
        {
            Email = email,
            ChangedAtUtc = changedAtUtc,
            IpAddress = ipAddress,
            FormattedChangedAt = changedAtUtc.ToString(isBg ? "dd.MM.yyyy HH:mm 'UTC'" : "yyyy-MM-dd HH:mm 'UTC'"),
        };

        var html = await _renderer.RenderAsync("PasswordChanged", model, culture, cancellationToken);
        var subject = LocalizeSubject(culture, "Паролата ви беше променена", "Your password was changed");
        await SendInternalAsync(email, subject, html, cancellationToken);
    }

    public async Task SendAsync(string toEmail, string subject, string templateName, object model, string? culture, CancellationToken cancellationToken)
    {
        var html = await _renderer.RenderAsync(templateName, model, culture, cancellationToken);
        await SendInternalAsync(toEmail, subject, html, cancellationToken);
    }

    public Task SendHtmlAsync(string toEmail, string subject, string htmlBody, CancellationToken cancellationToken) =>
        SendInternalAsync(toEmail, subject, htmlBody, cancellationToken);

    private async Task SendInternalAsync(string toEmail, string subject, string htmlBody, CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            _log.LogWarning("Email sending disabled (Email:Enabled=false); would send '{Subject}' to {To}.", subject, toEmail);
            return;
        }

        if (string.IsNullOrWhiteSpace(_options.From.Email))
        {
            throw new InvalidOperationException("Email:From:Email is not configured.");
        }

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_options.From.DisplayName, _options.From.Email));
        message.To.Add(MailboxAddress.Parse(toEmail));
        message.Subject = subject;
        message.Body = new BodyBuilder { HtmlBody = htmlBody }.ToMessageBody();

        using var client = new SmtpClient();
        client.ServerCertificateValidationCallback = (_, _, _, errors) =>
            errors == SslPolicyErrors.None || _options.Smtp.AllowInvalidCertificate;

        var secureOptions = ParseSecureSocketOptions(_options.Smtp.SecureSocketOptions);
        var connectMode = _options.Smtp.EnableSsl ? secureOptions : SecureSocketOptions.None;

        try
        {
            await client.ConnectAsync(_options.Smtp.Host, _options.Smtp.Port, connectMode, cancellationToken);

            if (!string.IsNullOrEmpty(_options.Smtp.Username))
            {
                await client.AuthenticateAsync(_options.Smtp.Username, _options.Smtp.Password, cancellationToken);
            }

            await client.SendAsync(message, cancellationToken);
            await client.DisconnectAsync(quit: true, cancellationToken);

            _log.LogInformation("Email sent to {To} subject='{Subject}'.", toEmail, subject);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to send email to {To} subject='{Subject}' via {Host}:{Port}.",
                toEmail, subject, _options.Smtp.Host, _options.Smtp.Port);
            throw;
        }
    }

    private static string LocalizeSubject(string? culture, string bg, string en) =>
        string.Equals(culture, "en-GB", StringComparison.OrdinalIgnoreCase) ? en : bg;

    private static SecureSocketOptions ParseSecureSocketOptions(string? value) =>
        Enum.TryParse<SecureSocketOptions>(value, ignoreCase: true, out var parsed)
            ? parsed
            : SecureSocketOptions.StartTls;
}
