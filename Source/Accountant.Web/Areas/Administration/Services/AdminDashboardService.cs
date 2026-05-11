using Accountant.DataAccess;
using Accountant.DataAccess.Entities.Product;
using Microsoft.EntityFrameworkCore;

namespace Accountant.Web.Areas.Administration.Services;

/// All metric + chart queries for the admin landing. Scoped — uses the
/// request's `AccountantDbContext`. All queries use `AsNoTracking` and
/// project to flat DTOs (no entity hydration).
public sealed class AdminDashboardService
{
    private readonly AccountantDbContext _db;

    public AdminDashboardService(AccountantDbContext db)
    {
        _db = db;
    }

    public async Task<DashboardMetrics> LoadMetricsAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var sevenDaysAgo = now.AddDays(-7);

        var documentsThisMonth = await _db.Documents
            .AsNoTracking()
            .Where(d => d.CreatedAtUtc >= monthStart)
            .CountAsync(cancellationToken)
            .ConfigureAwait(false);

        var extractionStats = await _db.DocumentExtractions
            .AsNoTracking()
            .Where(e => e.StartedAtUtc >= monthStart)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Tokens = g.Sum(e => (long)e.TokensIn + e.TokensOut),
                Cost = g.Sum(e => e.EstimatedCostUsd),
            })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        var activeUsers = await _db.Documents
            .AsNoTracking()
            .Where(d => d.CreatedAtUtc >= sevenDaysAgo)
            .Select(d => d.UploadedByUserId)
            .Distinct()
            .CountAsync(cancellationToken)
            .ConfigureAwait(false);

        var failed = await _db.DocumentExtractions
            .AsNoTracking()
            .Where(e => e.StartedAtUtc >= monthStart && e.Status == DocumentExtractionStatus.Failed)
            .CountAsync(cancellationToken)
            .ConfigureAwait(false);

        return new DashboardMetrics(
            documentsThisMonth,
            extractionStats?.Tokens ?? 0L,
            extractionStats?.Cost ?? 0m,
            activeUsers,
            failed);
    }

    public async Task<IReadOnlyList<DocumentsPerDayPoint>> GetDocumentsPerDayAsync(
        int days, CancellationToken cancellationToken)
    {
        days = Math.Clamp(days, 1, 365);
        var since = DateTime.UtcNow.Date.AddDays(-(days - 1));

        var rows = await _db.Documents
            .AsNoTracking()
            .Where(d => d.CreatedAtUtc >= since)
            .GroupBy(d => d.CreatedAtUtc.Date)
            .Select(g => new { Date = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Fill missing dates with 0 so the chart shows a continuous line.
        var byDate = rows.ToDictionary(r => r.Date, r => r.Count);
        var points = new List<DocumentsPerDayPoint>(days);
        for (int i = 0; i < days; i++)
        {
            var day = since.AddDays(i);
            points.Add(new DocumentsPerDayPoint(
                day.ToString("yyyy-MM-dd"),
                byDate.TryGetValue(day, out var c) ? c : 0));
        }
        return points;
    }

    public Task<List<VendorSlice>> GetVendorDistributionAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        return _db.DocumentExtractions
            .AsNoTracking()
            .Where(e => e.StartedAtUtc >= monthStart && e.Status == DocumentExtractionStatus.Success)
            .GroupBy(e => e.Vendor)
            .Select(g => new VendorSlice(g.Key, g.Count()))
            .ToListAsync(cancellationToken);
    }

    public Task<List<VendorLatency>> GetAvgLatencyAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        return _db.DocumentExtractions
            .AsNoTracking()
            .Where(e => e.StartedAtUtc >= monthStart
                && e.Status == DocumentExtractionStatus.Success
                && e.LatencyMs != null)
            .GroupBy(e => e.Vendor)
            .Select(g => new VendorLatency(g.Key, (int)Math.Round(g.Average(x => (double)x.LatencyMs!.Value))))
            .ToListAsync(cancellationToken);
    }
}

public sealed record DashboardMetrics(
    int DocumentsThisMonth,
    long TotalTokensThisMonth,
    decimal TotalCostUsdThisMonth,
    int ActiveUsersLast7Days,
    int FailedExtractionsThisMonth);

public sealed record DocumentsPerDayPoint(string Date, int Count);
public sealed record VendorSlice(string Vendor, int Count);
public sealed record VendorLatency(string Vendor, int AvgLatencyMs);
