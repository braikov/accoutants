using Accountant.Identity.Models;

namespace Accountant.DataAccess.Entities.Product;

public enum TenantRole
{
    /// Can manage members + rename / delete tenant.
    Owner = 1,

    /// Regular member: full access to documents within the tenant, but
    /// cannot invite or remove other members.
    Member = 2
}

/// One row per (User, Tenant) — many-to-many join. Composite uniqueness
/// enforced via index on (UserId, TenantId).
public class TenantMembership
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;

    public int TenantId { get; set; }
    public Tenant Tenant { get; set; } = null!;

    public TenantRole Role { get; set; }

    public DateTime JoinedAtUtc { get; set; }
}
