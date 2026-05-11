using Accountant.DataAccess;
using Accountant.DataAccess.Entities.Product;
using Accountant.Web.Areas.App.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace Accountant.Web.Areas.App.Services;

/// Loads the full folder tree and the documents in the currently-selected
/// folder for the workspace page. All queries scoped to the active tenant —
/// callers must pass the tenant id explicitly. The middleware-resolved
/// active tenant is the only legal source.
public sealed class WorkspaceService
{
    private readonly AccountantDbContext _db;

    public WorkspaceService(AccountantDbContext db)
    {
        _db = db;
    }

    public async Task<WorkspaceViewModel> LoadAsync(
        int tenantId,
        string tenantName,
        int? currentFolderId,
        CancellationToken cancellationToken)
    {
        var folders = await _db.Folders
            .AsNoTracking()
            .Where(f => f.TenantId == tenantId)
            .OrderBy(f => f.Name)
            .Select(f => new FolderRow(f.Id, f.ParentFolderId, f.Name))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var root = BuildTree(folders);

        // Validate currentFolderId belongs to this tenant. Silently fall back
        // to the root when a stale or tampered id is supplied.
        if (currentFolderId is int fid && folders.All(f => f.Id != fid))
        {
            currentFolderId = null;
        }

        var currentName = "Всички документи";
        var breadcrumbs = Array.Empty<BreadcrumbItem>() as IReadOnlyList<BreadcrumbItem>;
        if (currentFolderId is int currentId)
        {
            (currentName, breadcrumbs) = BuildBreadcrumbs(currentId, folders);
        }

        var documents = await _db.Documents
            .AsNoTracking()
            .Where(d => d.TenantId == tenantId && d.FolderId == currentFolderId)
            .OrderByDescending(d => d.CreatedAtUtc)
            .Select(d => new DocumentRow
            {
                Id = d.Id,
                OriginalFileName = d.OriginalFileName,
                ContentType = d.ContentType,
                ByteSize = d.ByteSize,
                Status = d.Status,
                HasThumbnail = d.ThumbnailKey != null,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new WorkspaceViewModel
        {
            TenantId = tenantId,
            TenantName = tenantName,
            Root = root,
            CurrentFolderId = currentFolderId,
            CurrentFolderName = currentName,
            Breadcrumbs = breadcrumbs,
            Documents = documents,
        };
    }

    /// Used by the status-polling endpoint. Returns only `(id, status)`
    /// pairs so the client can update badges without re-rendering the grid.
    public Task<List<DocumentStatusRow>> GetStatusesAsync(
        int tenantId,
        int[] documentIds,
        CancellationToken cancellationToken)
    {
        // See note in DocumentsController.EnqueueExtraction: must be a List
        // so .NET 10 binds `Contains` to the LINQ overload, not the new
        // `ReadOnlySpan<int>.Contains` (EF Core can't translate the latter).
        var ids = documentIds.ToList();
        return _db.Documents
            .AsNoTracking()
            .Where(d => d.TenantId == tenantId && ids.Contains(d.Id))
            .Select(d => new DocumentStatusRow(d.Id, d.Status, d.ThumbnailKey != null))
            .ToListAsync(cancellationToken);
    }

    private static FolderNode BuildTree(IReadOnlyList<FolderRow> rows)
    {
        var nodesById = rows.ToDictionary(
            r => r.Id,
            r => new FolderNode { Id = r.Id, Name = r.Name });
        var root = new FolderNode { Id = null, Name = "Всички документи" };
        foreach (var row in rows)
        {
            var node = nodesById[row.Id];
            if (row.ParentFolderId is int parentId && nodesById.TryGetValue(parentId, out var parent))
            {
                parent.Children.Add(node);
            }
            else
            {
                root.Children.Add(node);
            }
        }
        SortChildren(root);
        return root;
    }

    private static void SortChildren(FolderNode node)
    {
        node.Children.Sort(static (a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
        foreach (var child in node.Children)
        {
            SortChildren(child);
        }
    }

    private static (string Name, IReadOnlyList<BreadcrumbItem> Crumbs) BuildBreadcrumbs(
        int currentFolderId,
        IReadOnlyList<FolderRow> rows)
    {
        var byId = rows.ToDictionary(r => r.Id);
        var crumbs = new List<BreadcrumbItem>();
        var id = (int?)currentFolderId;
        string currentName = "Всички документи";
        while (id is int n && byId.TryGetValue(n, out var row))
        {
            if (crumbs.Count == 0)
            {
                currentName = row.Name;
            }
            crumbs.Add(new BreadcrumbItem(row.Id, row.Name));
            id = row.ParentFolderId;
        }
        crumbs.Reverse();
        return (currentName, crumbs);
    }

    private sealed record FolderRow(int Id, int? ParentFolderId, string Name);
}

public sealed record DocumentStatusRow(int Id, DocumentStatus Status, bool HasThumbnail);
