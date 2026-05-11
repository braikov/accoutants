using Accountant.DataAccess.Entities.Product;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Accountant.DataAccess.Configurations.Product;

internal sealed class DocumentExtractionConfiguration : IEntityTypeConfiguration<DocumentExtraction>
{
    public void Configure(EntityTypeBuilder<DocumentExtraction> b)
    {
        b.ToTable("document_extractions");
        b.HasKey(x => x.Id);

        b.Property(x => x.Vendor).HasMaxLength(32).IsRequired();
        b.Property(x => x.ModelName).HasMaxLength(128).IsRequired();
        b.Property(x => x.PromptVersion).HasMaxLength(64).IsRequired();
        b.Property(x => x.StartedAtUtc).IsRequired();
        b.Property(x => x.EstimatedCostUsd).HasPrecision(12, 6);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        b.Property(x => x.FailureReason).HasMaxLength(2000);
        b.Property(x => x.JsonResult).HasColumnType("longtext");

        // Stats queries: per-vendor / per-day aggregations.
        b.HasIndex(x => new { x.Vendor, x.StartedAtUtc });
        b.HasIndex(x => x.Status);
    }
}
