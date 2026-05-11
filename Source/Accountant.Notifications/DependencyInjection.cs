using Braikov.Identity.Core.Abstractions;
using Braikov.Notifications.Core;
using Braikov.Notifications.DataAccess;
using Braikov.Notifications.Email;
using Braikov.Notifications.MySql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Accountant.Notifications;

public static class DependencyInjection
{
    /// Registers everything Braikov.Notifications needs to dispatch through
    /// the email channel using `Accountant.Email`. Caller must have already
    /// registered an `IEmailSender` (via `AddAccountantEmail` or
    /// `AddAccountantEmailNullSender`) before this call — without it the
    /// email channel sender is skipped and email notifications silently no-op.
    ///
    /// Note: the three auth-flow NotificationTypeDefinitions (email_confirmation,
    /// password_reset, password_changed) are NOT registered here anymore — they
    /// live in `Braikov.Identity.Notifications` package, seeded via
    /// `.UseNotificationDispatcher()` on the IdentityBuilder.
    public static IServiceCollection AddAccountantNotifications(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<AccountantNotificationOptions>(
            configuration.GetSection(AccountantNotificationOptions.SectionName));

        // Persistence + core services. Connection string name "Accountant"
        // matches the one Web/Program.cs uses for the main DbContext, so the
        // notification tables live in the same DB as Identity + business.
        services.AddMySqlNotifications(configuration, connectionStringName: "Accountant");
        services.AddBraikovNotificationServices();

        services.AddScoped<IRecipientResolver, AccountantRecipientResolver>();

        // Only register the email channel sender when an IEmailSender exists.
        // This lets the host opt out (e.g. test rigs) by simply not calling
        // AddAccountantEmail before us.
        if (services.Any(descriptor => descriptor.ServiceType == typeof(IEmailSender)))
        {
            services.AddScoped<INotificationEmailSender, AccountantNotificationEmailSender>();
            services.AddScoped<IChannelSender, EmailChannelSender>();
        }

        return services;
    }
}
