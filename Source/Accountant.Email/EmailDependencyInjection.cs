using Accountant.Email.Services;
using Braikov.Identity.Core.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Accountant.Email;

public static class EmailDependencyInjection
{
    /// Wire SMTP-backed email pipeline. Reads `EmailOptions` from the `Email`
    /// section of configuration. Use this in Test / Production.
    public static IServiceCollection AddAccountantEmail(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<EmailOptions>(configuration.GetSection(EmailOptions.SectionName));
        services.AddSingleton<IEmailTemplateRenderer, RazorEmailTemplateRenderer>();
        services.AddScoped<IEmailSender, SmtpEmailSender>();
        return services;
    }

    /// Wire a logging-only sender (no SMTP IO). Use in Development /
    /// AutomatedTest. The renderer is still real — useful for catching template
    /// errors without sending anything.
    public static IServiceCollection AddAccountantEmailNullSender(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<EmailOptions>(configuration.GetSection(EmailOptions.SectionName));
        services.AddSingleton<IEmailTemplateRenderer, RazorEmailTemplateRenderer>();
        services.AddScoped<IEmailSender, NullEmailSender>();
        return services;
    }
}
