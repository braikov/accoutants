using Accountant.DataAccess.Entities.Product;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Accountant.DataAccess.Configurations.Product;

internal sealed class ApplicationSettingConfiguration : IEntityTypeConfiguration<ApplicationSetting>
{
    public void Configure(EntityTypeBuilder<ApplicationSetting> b)
    {
        b.ToTable("application_settings");
        b.HasKey(x => x.Id);

        b.Property(x => x.Key).IsRequired().HasMaxLength(120);
        b.HasIndex(x => x.Key).IsUnique();
        b.Property(x => x.Value).IsRequired().HasColumnType("text");
        b.Property(x => x.UpdatedAtUtc).IsRequired();

        b.HasOne(x => x.UpdatedByUser)
         .WithMany()
         .HasForeignKey(x => x.UpdatedByUserId)
         .OnDelete(DeleteBehavior.SetNull);
    }
}
