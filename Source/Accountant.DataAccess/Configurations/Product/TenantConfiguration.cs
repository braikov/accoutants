using Accountant.DataAccess.Entities.Product;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Accountant.DataAccess.Configurations.Product;

internal sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> b)
    {
        b.ToTable("tenants");
        b.HasKey(x => x.Id);

        b.Property(x => x.Name).HasMaxLength(256).IsRequired();
        b.Property(x => x.CreatedAtUtc).IsRequired();

        b.HasOne(x => x.OwnerUser)
         .WithMany()
         .HasForeignKey(x => x.OwnerUserId)
         .OnDelete(DeleteBehavior.SetNull);

        b.HasIndex(x => x.OwnerUserId);
    }
}
