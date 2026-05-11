using Accountant.DataAccess.Entities.Product;
using Accountant.Web.Areas.Administration.Services;

namespace Accountant.Web.Areas.Administration.ViewModels;

public sealed class DocumentsListViewModel
{
    public DocumentsFilter Filter { get; init; } = new();
    public PagedResult<DocumentAdminRow> Page { get; init; } = new(Array.Empty<DocumentAdminRow>(), 1, 50, 0);

    /// All tenants — populates the filter dropdown.
    public IReadOnlyList<(int Id, string Name)> Tenants { get; init; } = Array.Empty<(int, string)>();
    public IReadOnlyList<(int Id, string Email)> Users { get; init; } = Array.Empty<(int, string)>();
}

public sealed class DocumentsFilter
{
    public int? TenantId { get; set; }
    public int? UserId { get; set; }
    public string? Vendor { get; set; }
    public DocumentStatus? Status { get; set; }
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}

public sealed record DocumentAdminRow(
    int Id,
    string OriginalFileName,
    string ContentType,
    bool HasThumbnail,
    DocumentStatus Status,
    DateTime CreatedAtUtc,
    DateTime? ProcessedAtUtc,
    string TenantName,
    string UploadedByEmail,
    string? Vendor,
    string? ModelName,
    int? LatencyMs,
    decimal? CostUsd);

public sealed class UsersListViewModel
{
    public PagedResult<UserAdminRow> Page { get; init; } = new(Array.Empty<UserAdminRow>(), 1, 50, 0);
}

public sealed record UserAdminRow(
    int Id,
    string Email,
    DateTime? LockoutEndUtc,
    bool EmailConfirmed,
    int TenantsCount,
    int DocumentsCount,
    long Tokens,
    decimal CostUsd);

public sealed class TenantsListViewModel
{
    public PagedResult<TenantAdminRow> Page { get; init; } = new(Array.Empty<TenantAdminRow>(), 1, 50, 0);
}

public sealed record TenantAdminRow(
    int Id,
    string Name,
    string? OwnerEmail,
    int MembersCount,
    int DocumentsCount,
    decimal CostUsd,
    DateTime CreatedAtUtc);
