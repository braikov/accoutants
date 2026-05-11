using Accountant.DataAccess.Entities.Product;

namespace Accountant.Web.Areas.App.ViewModels;

/// Root view model for the workspace page (folder tree + documents grid).
public sealed class WorkspaceViewModel
{
    public required int TenantId { get; init; }
    public required string TenantName { get; init; }

    /// Tenant root pseudo-node. `Id == null` represents "no folder".
    public required FolderNode Root { get; init; }

    /// Folder currently selected (null = tenant root).
    public int? CurrentFolderId { get; init; }
    public string CurrentFolderName { get; init; } = "Всички документи";

    /// Path from tenant root to the current folder (excluding root). Empty
    /// when the user is at the tenant root.
    public IReadOnlyList<BreadcrumbItem> Breadcrumbs { get; init; } = Array.Empty<BreadcrumbItem>();

    public IReadOnlyList<DocumentRow> Documents { get; init; } = Array.Empty<DocumentRow>();
}

public sealed class FolderNode
{
    public int? Id { get; init; }
    public required string Name { get; init; }
    public List<FolderNode> Children { get; init; } = new();
}

public sealed record BreadcrumbItem(int Id, string Name);

public sealed class DocumentRow
{
    public required int Id { get; init; }
    public required string OriginalFileName { get; init; }
    public required string ContentType { get; init; }
    public long ByteSize { get; init; }
    public DocumentStatus Status { get; init; }
    public bool HasThumbnail { get; init; }
}
