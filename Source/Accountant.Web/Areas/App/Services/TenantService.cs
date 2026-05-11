using Accountant.DataAccess;
using Accountant.DataAccess.Entities.Product;
using Microsoft.EntityFrameworkCore;

namespace Accountant.Web.Areas.App.Services;

/// All tenant lifecycle operations. Used by `ActiveTenantMiddleware` to
/// bootstrap the user's default tenant on first request, and by
/// `TenantsController` for the user-facing list / create / switch flows.
public sealed class TenantService
{
    private readonly AccountantDbContext _db;

    public TenantService(AccountantDbContext db)
    {
        _db = db;
    }

    /// Tenants the user is a member of, oldest first.
    public Task<List<TenantSummary>> ListMembershipsAsync(int userId, CancellationToken cancellationToken)
        => _db.TenantMemberships
            .AsNoTracking()
            .Where(m => m.UserId == userId)
            .OrderBy(m => m.JoinedAtUtc)
            .Select(m => new TenantSummary(m.TenantId, m.Tenant.Name, m.Role))
            .ToListAsync(cancellationToken);

    /// Verifies that the user is a member of the given tenant. Used by the
    /// active-tenant middleware to reject tampered cookies.
    public Task<bool> IsMemberAsync(int userId, int tenantId, CancellationToken cancellationToken)
        => _db.TenantMemberships
            .AnyAsync(m => m.UserId == userId && m.TenantId == tenantId, cancellationToken);

    /// Creates a new tenant owned by `userId`. Membership Role=Owner.
    public async Task<Tenant> CreateTenantAsync(
        int userId,
        string name,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var tenant = new Tenant
        {
            Name = name,
            OwnerUserId = userId,
            CreatedAtUtc = now,
        };
        _db.Tenants.Add(tenant);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        _db.TenantMemberships.Add(new TenantMembership
        {
            UserId = userId,
            TenantId = tenant.Id,
            Role = TenantRole.Owner,
            JoinedAtUtc = now,
        });
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return tenant;
    }

    /// If the user has no memberships, creates a default tenant named
    /// "<email>'s firm" and adds them as Owner. Returns the tenant to make
    /// active. Idempotent — when called for a user with existing
    /// memberships, returns the first one.
    public async Task<TenantSummary> EnsureDefaultTenantAsync(
        int userId,
        string email,
        CancellationToken cancellationToken)
    {
        var existing = await _db.TenantMemberships
            .AsNoTracking()
            .Where(m => m.UserId == userId)
            .OrderBy(m => m.JoinedAtUtc)
            .Select(m => new TenantSummary(m.TenantId, m.Tenant.Name, m.Role))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            return existing;
        }

        var localPart = email.Contains('@', StringComparison.Ordinal)
            ? email[..email.IndexOf('@', StringComparison.Ordinal)]
            : email;
        var tenant = await CreateTenantAsync(
            userId,
            $"{localPart}'s firm",
            cancellationToken).ConfigureAwait(false);
        return new TenantSummary(tenant.Id, tenant.Name, TenantRole.Owner);
    }
}

public sealed record TenantSummary(int Id, string Name, TenantRole Role);
