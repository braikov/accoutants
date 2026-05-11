using Accountant.DataAccess;
using Accountant.DataAccess.Entities.Product;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Accountant.Jobs;

/// On Web startup, finds documents stuck in `Processing` and re-enqueues
/// them. The architecture runs Hangfire workers in-process with Kestrel, so
/// any `Processing` document after a restart is necessarily an orphan from
/// a previous run (the worker that owned it is gone). Re-enqueuing is safe
/// because `ExtractDocumentJob` is idempotent — it short-circuits when the
/// document is already in `Extracted` status.
///
/// Single-instance deploys only. If we ever run multiple Web nodes sharing
/// the same MySQL Hangfire schema, this needs a leader election guard.
public sealed class StuckJobRecoveryService : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IBackgroundJobClient _jobs;
    private readonly ILogger<StuckJobRecoveryService> _logger;

    public StuckJobRecoveryService(
        IServiceScopeFactory scopeFactory,
        IBackgroundJobClient jobs,
        ILogger<StuckJobRecoveryService> logger)
    {
        _scopeFactory = scopeFactory;
        _jobs = jobs;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AccountantDbContext>();

            var stuck = await db.Documents
                .Where(d => d.Status == DocumentStatus.Processing)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            if (stuck.Count == 0)
            {
                return;
            }

            foreach (var doc in stuck)
            {
                doc.Status = DocumentStatus.Queued;
            }
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            foreach (var doc in stuck)
            {
                _jobs.Enqueue<ExtractDocumentJob>(job => job.RunAsync(doc.Id, CancellationToken.None));
            }

            _logger.LogInformation(
                "Re-enqueued {Count} document(s) stuck in Processing from a previous run.",
                stuck.Count);
        }
        catch (Exception ex)
        {
            // Recovery is best-effort — never block app startup on it.
            _logger.LogError(ex, "StuckJobRecoveryService failed during startup.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
