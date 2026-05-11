using Accountant.DataAccess.Entities.Product;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Accountant.DataAccess.Configurations.Product;

internal sealed class DocumentConfiguration : IEntityTypeConfiguration<Document>
{
    public void Configure(EntityTypeBuilder<Document> b)
    {
        b.ToTable("documents");
        b.HasKey(x => x.Id);

        b.Property(x => x.OriginalFileName).HasMaxLength(512).IsRequired();
        b.Property(x => x.ContentType).HasMaxLength(128).IsRequired();
        b.Property(x => x.StorageKey).HasMaxLength(512).IsRequired();
        b.Property(x => x.ThumbnailKey).HasMaxLength(512);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        b.Property(x => x.CreatedAtUtc).IsRequired();

        b.HasOne(x => x.Tenant)
         .WithMany(t => t.Documents)
         .HasForeignKey(x => x.TenantId)
         .OnDelete(DeleteBehavior.Cascade);

        b.HasOne(x => x.Folder)
         .WithMany(f => f.Documents)
         .HasForeignKey(x => x.FolderId)
         .OnDelete(DeleteBehavior.SetNull);
        // Folder deletion moves docs to root rather than deleting them.

        b.HasOne(x => x.UploadedByUser)
         .WithMany()
         .HasForeignKey(x => x.UploadedByUserId)
         .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.Extraction)
         .WithOne(e => e.Document)
         .HasForeignKey<DocumentExtraction>(e => e.DocumentId)
         .OnDelete(DeleteBehavior.Cascade);

        b.HasMany(x => x.Corrections)
         .WithOne(c => c.Document)
         .HasForeignKey(c => c.DocumentId)
         .OnDelete(DeleteBehavior.Cascade);

        // Workspace queries: list documents in a folder.
        b.HasIndex(x => new { x.TenantId, x.FolderId, x.CreatedAtUtc });
        // Queue queries: find Queued / Processing docs.
        b.HasIndex(x => x.Status);
    }
}
