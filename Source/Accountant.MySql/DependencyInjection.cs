using Accountant.DataAccess;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Accountant.MySql;

public static class DependencyInjection
{
    /// Registers `AccountantDbContext` against MySQL/MariaDB using the supplied
    /// connection string. Uses `MariaDbServerVersion.AutoDetect` so the provider
    /// matches whatever MariaDB/MySQL version the host is actually running.
    public static IServiceCollection AddAccountantMySql(
        this IServiceCollection services,
        string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("Connection string is required.", nameof(connectionString));

        var serverVersion = ServerVersion.AutoDetect(connectionString);

        services.AddDbContext<AccountantDbContext>(options =>
        {
            options.UseMySql(connectionString, serverVersion, mysql =>
            {
                mysql.MigrationsAssembly(typeof(DependencyInjection).Assembly.GetName().Name);
                mysql.MigrationsHistoryTable("__AccountantMigrationsHistory");
            });
        });

        return services;
    }
}
