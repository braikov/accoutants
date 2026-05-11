using Accountant.DataAccess.Entities.Product;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Accountant.DataAccess.Configurations.Product;

internal sealed class TenantMembershipConfiguration : IEntityTypeConfiguration<TenantMembership>
{
    public void Configure(EntityTypeBuilder<TenantMembership> b)
    {
        b.ToTable("tenant_memberships");
        b.HasKey(x => x.Id);

        b.Property(x => x.Role).HasConversion<string>().HasMaxLength(16).IsRequired();
        b.Property(x => x.JoinedAtUtc).IsRequired();

        b.HasOne(x => x.User)
         .WithMany()
         .HasForeignKey(x => x.UserId)
         .OnDelete(DeleteBehavior.Cascade);

        b.HasOne(x => x.Tenant)
         .WithMany(t => t.Memberships)
         .HasForeignKey(x => x.TenantId)
         .OnDelete(DeleteBehavior.Cascade);

        // One row per (User, Tenant). No duplicate memberships.
        b.HasIndex(x => new { x.UserId, x.TenantId }).IsUnique();
        b.HasIndex(x => x.TenantId);
    }
}
