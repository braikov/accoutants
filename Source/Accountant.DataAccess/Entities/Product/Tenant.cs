using Accountant.Identity.Models;

namespace Accountant.DataAccess.Entities.Product;

/// One accounting firm. Users are members via `TenantMembership` (n:n) — a
/// single user can serve multiple firms but works in one active tenant at
/// a time. Folders + Documents are tenant-scoped.
public class Tenant
{
    public int Id { get; set; }

    /// Display name. Default on auto-create after registration:
    /// `"<email>'s firm"`. Owner can rename.
    public required string Name { get; set; }

    /// The user who created this tenant. Owner role; can invite others.
    /// Nullable to allow tenant survival if owner is deleted (rare).
    public int? OwnerUserId { get; set; }
    public ApplicationUser? OwnerUser { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public List<TenantMembership> Memberships { get; set; } = new();
    public List<Folder> Folders { get; set; } = new();
    public List<Document> Documents { get; set; } = new();
}
