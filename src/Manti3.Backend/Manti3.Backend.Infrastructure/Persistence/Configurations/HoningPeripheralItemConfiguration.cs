using Manti3.Backend.Domain.Items;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Manti3.Backend.Infrastructure.Persistence.Configurations;

internal sealed class HoningPeripheralItemConfiguration : IEntityTypeConfiguration<HoningPeripheralItem>
{
    public void Configure(EntityTypeBuilder<HoningPeripheralItem> builder)
    {
        builder.HasNoKey();
        builder.ToTable("HoningPeripheralPage", t => t.ExcludeFromMigrations());
        builder.Property(i => i.ItemCode).HasColumnName("ItemCode");
    }
}
