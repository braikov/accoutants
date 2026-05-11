using Accountant.DataAccess;
using Accountant.DataAccess.Entities.Product;
using Accountant.Web.Areas.Administration.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace Accountant.Web.Areas.Administration.Services;

/// Cross-tenant catalog queries for the admin tables. All queries use
/// `AsNoTracking` and project to flat record DTOs.
public sealed class AdminCatalogService
{
    private const int MaxPageSize = 200;

    private readonly AccountantDbContext _db;

    public AdminCatalogService(AccountantDbContext db)
    {
        _db = db;
    }

    public async Task<PagedResult<DocumentAdminRow>> ListDocumentsAsync(
        DocumentsFilter filter,
        CancellationToken cancellationToken)
    {
        var page = Math.Max(1, filter.Page);
        var pageSize = Math.Clamp(filter.PageSize, 1, MaxPageSize);

        var query = _db.Documents.AsNoTracking().AsQueryable();
        if (filter.TenantId is int t) query = query.Where(d => d.TenantId == t);
        if (filter.UserId is int u) query = query.Where(d => d.UploadedByUserId == u);
        if (filter.Status is DocumentStatus s) query = query.Where(d => d.Status == s);
        if (filter.DateFrom is DateTime from)
        {
            var fromUtc = DateTime.SpecifyKind(from.Date, DateTimeKind.Utc);
            query = query.Where(d => d.CreatedAtUtc >= fromUtc);
        }
        if (filter.DateTo is DateTime to)
        {
            var toUtc = DateTime.SpecifyKind(to.Date.AddDays(1), DateTimeKind.Utc);
            query = query.Where(d => d.CreatedAtUtc < toUtc);
        }
        if (!string.IsNullOrWhiteSpace(filter.Vendor))
        {
            var vendor = filter.Vendor.Trim().ToLowerInvariant();
            query = query.Where(d => d.Extraction != null && d.Extraction.Vendor == vendor);
        }

        var total = await query.CountAsync(cancellationToken).ConfigureAwait(false);

        var items = await query
            .OrderByDescending(d => d.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(d => new DocumentAdminRow(
                d.Id,
                d.OriginalFileName,
                d.ContentType,
                d.ThumbnailKey != null,
                d.Status,
                d.CreatedAtUtc,
                d.ProcessedAtUtc,
                d.Tenant.Name,
                d.UploadedByUser.Email!,
                d.Extraction != null ? d.Extraction.Vendor : null,
                d.Extraction != null ? d.Extraction.ModelName : null,
                d.Extraction != null ? d.Extraction.LatencyMs : null,
                d.Extraction != null ? (decimal?)d.Extraction.EstimatedCostUsd : null))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new PagedResult<DocumentAdminRow>(items, page, pageSize, total);
    }

    public Task<List<(int Id, string Name)>> ListAllTenantsAsync(CancellationToken cancellationToken)
        => _db.Tenants
            .AsNoTracking()
            .OrderBy(t => t.Name)
            .Select(t => new ValueTuple<int, string>(t.Id, t.Name))
            .ToListAsync(cancellationToken);

    public Task<List<(int Id, string Email)>> ListAllUsersAsync(CancellationToken cancellationToken)
        => _db.Users
            .AsNoTracking()
            .OrderBy(u => u.Email)
            .Select(u => new ValueTuple<int, string>(u.Id, u.Email!))
            .ToListAsync(cancellationToken);

    public async Task<PagedResult<UserAdminRow>> ListUsersAsync(
        int page, int pageSize, CancellationToken cancellationToken)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);
        var total = await _db.Users.AsNoTracking().CountAsync(cancellationToken).ConfigureAwait(false);

        var items = await _db.Users
            .AsNoTracking()
            .OrderBy(u => u.Email)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(u => new
            {
                u.Id,
                u.Email,
                u.LockoutEnd,
                u.EmailConfirmed,
                TenantsCount = _db.TenantMemberships.Count(m => m.UserId == u.Id),
                DocumentsCount = _db.Documents.Count(d => d.UploadedByUserId == u.Id),
                Tokens = _db.Documents
                    .Where(d => d.UploadedByUserId == u.Id && d.Extraction != null)
                    .Sum(d => (long?)(d.Extraction!.TokensIn + d.Extraction.TokensOut)) ?? 0L,
                Cost = _db.Documents
                    .Where(d => d.UploadedByUserId == u.Id && d.Extraction != null)
                    .Sum(d => (decimal?)d.Extraction!.EstimatedCostUsd) ?? 0m,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var rows = items.Select(x => new UserAdminRow(
            x.Id, x.Email!, x.LockoutEnd?.UtcDateTime, x.EmailConfirmed,
            x.TenantsCount, x.DocumentsCount, x.Tokens, x.Cost)).ToList();

        return new PagedResult<UserAdminRow>(rows, page, pageSize, total);
    }

    public async Task<PagedResult<TenantAdminRow>> ListTenantsAsync(
        int page, int pageSize, CancellationToken cancellationToken)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);
        var total = await _db.Tenants.AsNoTracking().CountAsync(cancellationToken).ConfigureAwait(false);

        var items = await _db.Tenants
            .AsNoTracking()
            .OrderBy(t => t.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(t => new
            {
                t.Id,
                t.Name,
                OwnerEmail = t.OwnerUser != null ? t.OwnerUser.Email : null,
                t.CreatedAtUtc,
                MembersCount = _db.TenantMemberships.Count(m => m.TenantId == t.Id),
                DocumentsCount = _db.Documents.Count(d => d.TenantId == t.Id),
                Cost = _db.Documents
                    .Where(d => d.TenantId == t.Id && d.Extraction != null)
                    .Sum(d => (decimal?)d.Extraction!.EstimatedCostUsd) ?? 0m,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var rows = items.Select(x => new TenantAdminRow(
            x.Id, x.Name, x.OwnerEmail,
            x.MembersCount, x.DocumentsCount, x.Cost, x.CreatedAtUtc)).ToList();

        return new PagedResult<TenantAdminRow>(rows, page, pageSize, total);
    }
}
