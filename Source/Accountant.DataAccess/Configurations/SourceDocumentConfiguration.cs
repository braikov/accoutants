using Accountant.DataAccess.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Accountant.DataAccess.Configurations;

internal sealed class SourceDocumentConfiguration : IEntityTypeConfiguration<SourceDocument>
{
    public void Configure(EntityTypeBuilder<SourceDocument> b)
    {
        b.ToTable("source_documents");
        b.HasKey(x => x.Id);

        b.Property(x => x.FileHash).HasMaxLength(64).IsRequired();
        b.Property(x => x.FileName).HasMaxLength(512).IsRequired();
        b.Property(x => x.FileSizeBytes);
        b.Property(x => x.Width);
        b.Property(x => x.Height);
        b.Property(x => x.FirstSeenAtUtc);

        b.HasIndex(x => x.FileHash).IsUnique();
        b.HasIndex(x => x.FileName);

        b.HasMany(x => x.Extractions)
         .WithOne(x => x.SourceDocument)
         .HasForeignKey(x => x.SourceDocumentId)
         .OnDelete(DeleteBehavior.Cascade);

        b.HasOne(x => x.GroundTruth)
         .WithOne(x => x.SourceDocument)
         .HasForeignKey<GroundTruth>(x => x.SourceDocumentId)
         .OnDelete(DeleteBehavior.Cascade);
    }
}
