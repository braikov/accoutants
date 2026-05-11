using Accountant.Identity.Models;

namespace Accountant.DataAccess.Entities.Product;

/// Key/value bag for app-level settings the admin can change through the UI
/// without a redeploy. Values are stored as strings; callers parse them.
public class ApplicationSetting
{
    public int Id { get; set; }

    /// Dotted setting name, e.g. `Extraction.DefaultVendor`. Unique.
    public required string Key { get; set; }

    public required string Value { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public int? UpdatedByUserId { get; set; }
    public ApplicationUser? UpdatedByUser { get; set; }
}
