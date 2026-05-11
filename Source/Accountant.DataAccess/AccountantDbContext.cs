using Accountant.DataAccess.Entities;
using Accountant.DataAccess.Entities.Product;
using Accountant.Identity.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Accountant.DataAccess;

/// Single application DbContext. Three coexisting domains:
///
/// 1. Research entities (frozen reference): `SourceDocument`, `Extraction`,
///    `GroundTruth`, `EvaluationRun`, `EvaluationDocument`. Drive the
///    `Accountant.Processors` corpus + ground-truth tooling.
///
/// 2. Product entities (active client surface): `Tenant`, `TenantMembership`,
///    `Folder`, `Document`, `DocumentExtraction`, `DocumentCorrection`. Drive
///    `Areas/App` workspace + `Areas/Administration` dashboard.
///
/// 3. ASP.NET Core Identity tables (AspNetUsers / AspNetRoles / ...).
///
/// All three live in the same MySQL DB and the same context so a single
/// `dotnet ef database update` keeps the schema consistent and there is
/// one migration history table.
public class AccountantDbContext : IdentityDbContext<ApplicationUser, IdentityRole<int>, int>
{
    public AccountantDbContext(DbContextOptions<AccountantDbContext> options) : base(options) { }

    // Research entities.
    public DbSet<SourceDocument> SourceDocuments => Set<SourceDocument>();
    public DbSet<Extraction> Extractions => Set<Extraction>();
    public DbSet<GroundTruth> GroundTruths => Set<GroundTruth>();
    public DbSet<EvaluationRun> EvaluationRuns => Set<EvaluationRun>();
    public DbSet<EvaluationDocument> EvaluationDocuments => Set<EvaluationDocument>();

    // Product entities.
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<TenantMembership> TenantMemberships => Set<TenantMembership>();
    public DbSet<Folder> Folders => Set<Folder>();
    public DbSet<Document> Documents => Set<Document>();
    public DbSet<DocumentExtraction> DocumentExtractions => Set<DocumentExtraction>();
    public DbSet<DocumentCorrection> DocumentCorrections => Set<DocumentCorrection>();
    public DbSet<ApplicationSetting> ApplicationSettings => Set<ApplicationSetting>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        // Identity tables (AspNetUsers etc.) — must come first so our own
        // configurations can override columns / relationships if ever needed.
        base.OnModelCreating(mb);

        mb.ApplyConfigurationsFromAssembly(typeof(AccountantDbContext).Assembly);
    }
}
