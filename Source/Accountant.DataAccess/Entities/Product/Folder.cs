using Accountant.Identity.Models;

namespace Accountant.DataAccess.Entities.Product;

/// One node in a tenant's free-form folder tree. Root folders have
/// `ParentFolderId == null`. Names are unique per (TenantId, ParentFolderId)
/// — i.e. you can't have two "January" folders under the same parent, but
/// you can have one under each of "2024" and "2025".
public class Folder
{
    public int Id { get; set; }

    public int TenantId { get; set; }
    public Tenant Tenant { get; set; } = null!;

    public int? ParentFolderId { get; set; }
    public Folder? ParentFolder { get; set; }
    public List<Folder> Children { get; set; } = new();

    public required string Name { get; set; }

    public int CreatedByUserId { get; set; }
    public ApplicationUser CreatedByUser { get; set; } = null!;

    public DateTime CreatedAtUtc { get; set; }

    public List<Document> Documents { get; set; } = new();
}
