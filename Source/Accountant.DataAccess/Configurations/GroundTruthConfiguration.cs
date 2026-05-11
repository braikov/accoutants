using Accountant.DataAccess.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Accountant.DataAccess.Configurations;

internal sealed class GroundTruthConfiguration : IEntityTypeConfiguration<GroundTruth>
{
    public void Configure(EntityTypeBuilder<GroundTruth> b)
    {
        b.ToTable("ground_truths");
        b.HasKey(x => x.Id);

        b.Property(x => x.ExtractionJson).HasColumnType("longtext").IsRequired();
        b.Property(x => x.LastEditedAtUtc);
        b.Property(x => x.LastEditedBy).HasMaxLength(128);

        b.HasIndex(x => x.SourceDocumentId).IsUnique();
    }
}
