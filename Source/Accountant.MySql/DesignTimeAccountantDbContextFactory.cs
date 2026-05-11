using Accountant.DataAccess;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Accountant.MySql;

/// Used by `dotnet ef` at design time (migrations add / database update) to construct
/// the DbContext without spinning up the host. Connection string is taken from the first
/// CLI arg if present, otherwise from `ACCOUNTANT_DB_CONNECTION` env var, otherwise from
/// a localhost design-time fallback.
public sealed class DesignTimeAccountantDbContextFactory
    : IDesignTimeDbContextFactory<AccountantDbContext>
{
    public AccountantDbContext CreateDbContext(string[] args)
    {
        var connectionString = args.Length > 0
            ? args[0]
            : Environment.GetEnvironmentVariable("ACCOUNTANT_DB_CONNECTION")
              ?? "Server=localhost;Port=3306;Database=accountant_design;User=root;Password=;";

        var options = new DbContextOptionsBuilder<AccountantDbContext>()
            .UseMySql(
                connectionString,
                new MariaDbServerVersion(new Version(10, 4, 0)),
                mysql => mysql
                    .MigrationsAssembly(typeof(DependencyInjection).Assembly.FullName)
                    .MigrationsHistoryTable("__AccountantMigrationsHistory"))
            .Options;

        return new AccountantDbContext(options);
    }
}
