using Braikov.Identity.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace Accountant.Email.Services;

/// `IEmailSender` that never touches SMTP — just logs the structured intent.
/// Wire this in Development / AutomatedTest where you don't want real mail.
/// The log includes the callback URL so you can complete email-confirmation
/// or password-reset flows manually from the console output.
public class NullEmailSender : IEmailSender
{
    private readonly ILogger<NullEmailSender> _log;

    public NullEmailSender(ILogger<NullEmailSender> log) { _log = log; }

    public Task SendEmailConfirmationAsync(string email, string callbackUrl, string? shortCode, string? culture, CancellationToken cancellationToken)
    {
        _log.LogWarning("[NullEmailSender] EmailConfirmation -> {To} | callback: {CallbackUrl} | code: {Code}", email, callbackUrl, shortCode);
        return Task.CompletedTask;
    }

    public Task SendPasswordResetAsync(string email, string callbackUrl, string? shortCode, string? culture, CancellationToken cancellationToken)
    {
        _log.LogWarning("[NullEmailSender] PasswordReset -> {To} | callback: {CallbackUrl} | code: {Code}", email, callbackUrl, shortCode);
        return Task.CompletedTask;
    }

    public Task SendPasswordChangedAsync(string email, DateTime changedAtUtc, string ipAddress, string? culture, CancellationToken cancellationToken)
    {
        _log.LogWarning("[NullEmailSender] PasswordChanged -> {To} at {ChangedAtUtc} from {Ip}", email, changedAtUtc, ipAddress);
        return Task.CompletedTask;
    }

    public Task SendAsync(string toEmail, string subject, string templateName, object model, string? culture, CancellationToken cancellationToken)
    {
        _log.LogWarning("[NullEmailSender] {Template} -> {To} subject='{Subject}'", templateName, toEmail, subject);
        return Task.CompletedTask;
    }

    public Task SendHtmlAsync(string toEmail, string subject, string htmlBody, CancellationToken cancellationToken)
    {
        _log.LogWarning("[NullEmailSender] raw-html -> {To} subject='{Subject}' ({BodyLength} chars)", toEmail, subject, htmlBody?.Length ?? 0);
        return Task.CompletedTask;
    }
}
