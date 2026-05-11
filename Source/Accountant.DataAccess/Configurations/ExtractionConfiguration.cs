using Accountant.DataAccess.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Accountant.DataAccess.Configurations;

internal sealed class ExtractionConfiguration : IEntityTypeConfiguration<Extraction>
{
    public void Configure(EntityTypeBuilder<Extraction> b)
    {
        b.ToTable("extractions");
        b.HasKey(x => x.Id);

        b.Property(x => x.Vendor).HasMaxLength(32).IsRequired();
        b.Property(x => x.Model).HasMaxLength(128).IsRequired();
        b.Property(x => x.PromptVersion).HasMaxLength(128);
        b.Property(x => x.Pipeline).HasMaxLength(32).IsRequired();
        b.Property(x => x.SchemaVersion).HasMaxLength(64).IsRequired();
        b.Property(x => x.OcrUsed);
        b.Property(x => x.StartedAtUtc);
        b.Property(x => x.DurationMs);
        b.Property(x => x.InputTokens);
        b.Property(x => x.OutputTokens);
        b.Property(x => x.CostEstimateUsd).HasMaxLength(32);
        b.Property(x => x.StopReason).HasMaxLength(64);
        b.Property(x => x.IsSuccess);
        b.Property(x => x.ErrorMessage).HasColumnType("text");
        b.Property(x => x.ValidationNeedsReview);
        b.Property(x => x.ValidationFailCount);
        b.Property(x => x.ValidationWarnCount);
        b.Property(x => x.DocumentType).HasMaxLength(32);

        b.Property(x => x.ResultJson).HasColumnType("longtext").IsRequired();

        b.HasIndex(x => new { x.SourceDocumentId, x.Vendor, x.StartedAtUtc });
        b.HasIndex(x => x.PromptVersion);
        b.HasIndex(x => x.Model);
    }
}
