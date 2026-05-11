using Accountant.Jobs.Extraction;
using Hangfire;
using Hangfire.MySql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Accountant.Jobs;

public static class JobsDependencyInjection
{
    /// Registers Hangfire (MySQL storage), the admin dashboard authorization
    /// filter, and the extraction job + its factory dependency.
    public static IServiceCollection AddAccountantJobs(
        this IServiceCollection services,
        IConfiguration configuration,
        string defaultConnectionString)
    {
        services.Configure<HangfireOptions>(configuration.GetSection(HangfireOptions.SectionName));
        var hangfireOptions = configuration.GetSection(HangfireOptions.SectionName)
            .Get<HangfireOptions>() ?? new HangfireOptions();
        var connectionString = string.IsNullOrWhiteSpace(hangfireOptions.ConnectionString)
            ? defaultConnectionString
            : hangfireOptions.ConnectionString;

        services.AddHangfire(cfg =>
        {
            cfg.SetDataCompatibilityLevel(CompatibilityLevel.Version_180);
            cfg.UseSimpleAssemblyNameTypeSerializer();
            cfg.UseRecommendedSerializerSettings();
            cfg.UseStorage(new MySqlStorage(connectionString, new MySqlStorageOptions
            {
                TablesPrefix = "Hangfire_",
                PrepareSchemaIfNecessary = true,
                QueuePollInterval = TimeSpan.FromSeconds(5),
                JobExpirationCheckInterval = TimeSpan.FromHours(1),
                CountersAggregateInterval = TimeSpan.FromMinutes(5),
                DashboardJobListLimit = 50_000,
                TransactionTimeout = TimeSpan.FromMinutes(1),
            }));
        });

        services.AddHangfireServer(opts =>
        {
            opts.WorkerCount = Math.Max(2, Environment.ProcessorCount);
            opts.ServerName = $"accountant-{Environment.MachineName}";
        });

        services.AddSingleton<IExtractorFactory, ExtractorFactory>();
        services.AddSingleton<AdminDashboardAuthorizationFilter>();
        services.AddScoped<ExtractDocumentJob>();
        return services;
    }
}
