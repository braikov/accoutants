using System.Text.Json;
using Accountant.Email.Models;
using Braikov.Identity.Core.Abstractions;
using Braikov.Identity.Notifications;
using Braikov.Notifications.Email;

namespace Accountant.Notifications;

/// Bridges Braikov's email channel into `Accountant.Email.IEmailSender`,
/// so notifications dispatched through `INotificationService` end up routed
/// through our Razor template renderer + SMTP transport.
///
/// For auth flows we dispatch on TemplateKey and call the typed methods
/// (SendEmailConfirmationAsync etc.) so the email subject comes out
/// localized (BG) — going through the generic `SendAsync` would inherit
/// the notification's English audit Title as the subject.
public sealed class AccountantNotificationEmailSender : INotificationEmailSender
{
    private readonly IEmailSender emailSender;

    public AccountantNotificationEmailSender(IEmailSender emailSender)
    {
        this.emailSender = emailSender;
    }

    public async Task<string?> SendAsync(
        EmailNotificationMessage message,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(message.TemplateKey))
        {
            throw new InvalidOperationException(
                "Accountant notification email delivery requires a template key. " +
                "Register the notification type via NotificationTypeDefinition with " +
                "a TemplateKeysByChannel[Email] entry.");
        }

        switch (message.TemplateKey)
        {
            case BraikovIdentityEmailTemplateKeys.EmailConfirmation:
            {
                var model = ResolvePayload<EmailConfirmationModel>(message);
                await emailSender.SendEmailConfirmationAsync(
                    model.Email, model.CallbackUrl, model.ShortCode, message.Culture, cancellationToken);
                break;
            }
            case BraikovIdentityEmailTemplateKeys.PasswordReset:
            {
                var model = ResolvePayload<PasswordResetModel>(message);
                await emailSender.SendPasswordResetAsync(
                    model.Email, model.CallbackUrl, model.ShortCode, message.Culture, cancellationToken);
                break;
            }
            case BraikovIdentityEmailTemplateKeys.PasswordChanged:
            {
                var model = ResolvePayload<PasswordChangedModel>(message);
                await emailSender.SendPasswordChangedAsync(
                    model.Email, model.ChangedAtUtc, model.IpAddress, message.Culture, cancellationToken);
                break;
            }
            default:
            {
                // Unknown template key — fall back to the generic SendAsync
                // path. Subject comes from the notification's Title (host
                // controls it via NotificationTypeDefinition or request).
                var model = message.TemplateModel ?? new { };
                await emailSender.SendAsync(
                    message.To, message.Subject, message.TemplateKey,
                    model, message.Culture, cancellationToken);
                break;
            }
        }

        return null;
    }

    private static T ResolvePayload<T>(EmailNotificationMessage message)
        where T : new()
    {
        if (message.TemplateModel is T typed) return typed;
        if (string.IsNullOrWhiteSpace(message.PayloadJson)) return new T();
        return JsonSerializer.Deserialize<T>(message.PayloadJson) ?? new T();
    }
}
