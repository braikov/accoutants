using Accountant.DataAccess.Entities.Product;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Accountant.DataAccess.Configurations.Product;

internal sealed class FolderConfiguration : IEntityTypeConfiguration<Folder>
{
    public void Configure(EntityTypeBuilder<Folder> b)
    {
        b.ToTable("folders");
        b.HasKey(x => x.Id);

        b.Property(x => x.Name).HasMaxLength(256).IsRequired();
        b.Property(x => x.CreatedAtUtc).IsRequired();

        b.HasOne(x => x.Tenant)
         .WithMany(t => t.Folders)
         .HasForeignKey(x => x.TenantId)
         .OnDelete(DeleteBehavior.Cascade);

        b.HasOne(x => x.ParentFolder)
         .WithMany(f => f.Children)
         .HasForeignKey(x => x.ParentFolderId)
         .OnDelete(DeleteBehavior.Restrict);
        // Restrict not Cascade: deleting a folder with children must be
        // explicit (server-side recursive delete in the service layer).

        b.HasOne(x => x.CreatedByUser)
         .WithMany()
         .HasForeignKey(x => x.CreatedByUserId)
         .OnDelete(DeleteBehavior.Restrict);

        // Tree-browse + uniqueness within a parent.
        b.HasIndex(x => new { x.TenantId, x.ParentFolderId });
        b.HasIndex(x => new { x.TenantId, x.ParentFolderId, x.Name }).IsUnique();
    }
}
