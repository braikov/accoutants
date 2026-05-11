using Accountant.Identity.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Accountant.Web.Areas.Administration;

/// On Web startup:
/// 1. Ensures the "Admin" role exists.
/// 2. If no user is in the Admin role yet, promotes the lowest-UserId user.
///
/// Idempotent — does nothing once an admin user exists. Lets the first
/// registered account log into `/Administration/*` without manual SQL.
public sealed class AdminRoleBootstrapService : IHostedService
{
    private const string AdminRole = "Admin";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AdminRoleBootstrapService> _logger;

    public AdminRoleBootstrapService(
        IServiceScopeFactory scopeFactory,
        ILogger<AdminRoleBootstrapService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<int>>>();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

            if (!await roles.RoleExistsAsync(AdminRole).ConfigureAwait(false))
            {
                await roles.CreateAsync(new IdentityRole<int>(AdminRole)).ConfigureAwait(false);
                _logger.LogInformation("Created `Admin` role.");
            }

            var existingAdmins = await users.GetUsersInRoleAsync(AdminRole).ConfigureAwait(false);
            if (existingAdmins.Count > 0)
            {
                return;
            }

            var seed = await users.Users
                .OrderBy(u => u.Id)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);
            if (seed is null)
            {
                return;
            }

            var result = await users.AddToRoleAsync(seed, AdminRole).ConfigureAwait(false);
            if (result.Succeeded)
            {
                _logger.LogInformation(
                    "Promoted {Email} (UserId={UserId}) to Admin — first registered user.",
                    seed.Email, seed.Id);
            }
            else
            {
                _logger.LogWarning(
                    "Failed to promote {Email} to Admin: {Errors}",
                    seed.Email, string.Join("; ", result.Errors.Select(e => e.Description)));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AdminRoleBootstrapService failed during startup.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
