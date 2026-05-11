using Accountant.DataAccess.Entities.Product;
using Microsoft.EntityFrameworkCore;

namespace Accountant.DataAccess.Services;

/// Reads + writes app-level settings stored in the `application_settings`
/// table. String-valued; callers parse if needed. Lookups are not cached —
/// the table is tiny and reads happen once per request / once per job run.
public interface IAppSettingsService
{
    Task<string?> GetAsync(string key, CancellationToken cancellationToken);

    Task SetAsync(string key, string value, int? updatedByUserId, CancellationToken cancellationToken);
}

public static class AppSettingKeys
{
    /// One of `claude` / `codex` / `gemini`. Read by `ExtractDocumentJob` at
    /// run time so admin can switch vendors without a redeploy.
    public const string ExtractionDefaultVendor = "Extraction.DefaultVendor";
}

public sealed class AppSettingsService : IAppSettingsService
{
    private readonly AccountantDbContext _db;

    public AppSettingsService(AccountantDbContext db)
    {
        _db = db;
    }

    public Task<string?> GetAsync(string key, CancellationToken cancellationToken)
        => _db.ApplicationSettings
            .AsNoTracking()
            .Where(s => s.Key == key)
            .Select(s => s.Value)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task SetAsync(string key, string value, int? updatedByUserId, CancellationToken cancellationToken)
    {
        var existing = await _db.ApplicationSettings
            .FirstOrDefaultAsync(s => s.Key == key, cancellationToken)
            .ConfigureAwait(false);
        if (existing is null)
        {
            _db.ApplicationSettings.Add(new ApplicationSetting
            {
                Key = key,
                Value = value,
                UpdatedAtUtc = DateTime.UtcNow,
                UpdatedByUserId = updatedByUserId,
            });
        }
        else
        {
            existing.Value = value;
            existing.UpdatedAtUtc = DateTime.UtcNow;
            existing.UpdatedByUserId = updatedByUserId;
        }
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
