using Accountant.DataAccess.Entities;
using Accountant.Identity.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Accountant.DataAccess;

/// Single application DbContext: hosts both the business entities (SourceDocument,
/// Extraction, GroundTruth, EvaluationRun, EvaluationDocument) and the ASP.NET
/// Core Identity tables (AspNetUsers / AspNetRoles / AspNetUserClaims / ...).
///
/// Per task 0002 design decision: identity tables live in the same MySQL DB and
/// the same context as business data, so a single `dotnet ef database update`
/// keeps the schema consistent and there is one migration history table.
public class AccountantDbContext : IdentityDbContext<ApplicationUser, IdentityRole<int>, int>
{
    public AccountantDbContext(DbContextOptions<AccountantDbContext> options) : base(options) { }

    public DbSet<SourceDocument> SourceDocuments => Set<SourceDocument>();
    public DbSet<Extraction> Extractions => Set<Extraction>();
    public DbSet<GroundTruth> GroundTruths => Set<GroundTruth>();
    public DbSet<EvaluationRun> EvaluationRuns => Set<EvaluationRun>();
    public DbSet<EvaluationDocument> EvaluationDocuments => Set<EvaluationDocument>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        // Identity tables (AspNetUsers etc.) — must come first so our own
        // configurations can override columns / relationships if ever needed.
        base.OnModelCreating(mb);

        mb.ApplyConfigurationsFromAssembly(typeof(AccountantDbContext).Assembly);
    }
}
