using Accountant.DataAccess.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Accountant.DataAccess.Configurations;

internal sealed class EvaluationRunConfiguration : IEntityTypeConfiguration<EvaluationRun>
{
    public void Configure(EntityTypeBuilder<EvaluationRun> b)
    {
        b.ToTable("evaluation_runs");
        b.HasKey(x => x.Id);

        b.Property(x => x.RunAtUtc);
        b.Property(x => x.Notes).HasMaxLength(512);

        b.HasMany(x => x.Documents)
         .WithOne(x => x.EvaluationRun)
         .HasForeignKey(x => x.EvaluationRunId)
         .OnDelete(DeleteBehavior.Cascade);

        b.HasIndex(x => x.RunAtUtc);
    }
}

internal sealed class EvaluationDocumentConfiguration : IEntityTypeConfiguration<EvaluationDocument>
{
    public void Configure(EntityTypeBuilder<EvaluationDocument> b)
    {
        b.ToTable("evaluation_documents");
        b.HasKey(x => x.Id);

        b.Property(x => x.Vendor).HasMaxLength(32).IsRequired();
        b.Property(x => x.PromptVersion).HasMaxLength(128);
        b.Property(x => x.Model).HasMaxLength(128);
        b.Property(x => x.MismatchesJson).HasColumnType("longtext").IsRequired();

        b.HasOne(x => x.SourceDocument)
         .WithMany()
         .HasForeignKey(x => x.SourceDocumentId)
         .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.Extraction)
         .WithMany()
         .HasForeignKey(x => x.ExtractionId)
         .OnDelete(DeleteBehavior.SetNull);

        b.HasIndex(x => new { x.EvaluationRunId, x.Vendor });
        b.HasIndex(x => x.PromptVersion);
    }
}
