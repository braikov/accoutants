using Accountant.DataAccess.Entities.Product;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Accountant.DataAccess.Configurations.Product;

internal sealed class DocumentCorrectionConfiguration : IEntityTypeConfiguration<DocumentCorrection>
{
    public void Configure(EntityTypeBuilder<DocumentCorrection> b)
    {
        b.ToTable("document_corrections");
        b.HasKey(x => x.Id);

        b.Property(x => x.EditedAtUtc).IsRequired();
        b.Property(x => x.CorrectedJson).HasColumnType("longtext").IsRequired();

        b.HasOne(x => x.EditedByUser)
         .WithMany()
         .HasForeignKey(x => x.EditedByUserId)
         .OnDelete(DeleteBehavior.Restrict);

        // "Latest correction for this document" — common lookup.
        b.HasIndex(x => new { x.DocumentId, x.EditedAtUtc });
    }
}
