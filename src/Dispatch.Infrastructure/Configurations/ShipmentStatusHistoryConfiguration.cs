using Dispatch.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dispatch.Infrastructure.Configurations;

internal sealed class ShipmentStatusHistoryConfiguration : IEntityTypeConfiguration<ShipmentStatusHistory>
{
    public void Configure(EntityTypeBuilder<ShipmentStatusHistory> builder)
    {
        builder.ToTable("ShipmentStatusHistory");

        builder.Property(h => h.FromStatus).HasConversion<string>().HasMaxLength(20).IsUnicode(false);
        builder.Property(h => h.ToStatus).HasConversion<string>().HasMaxLength(20).IsUnicode(false);
        builder.Property(h => h.ChangedAt).HasColumnType("datetime2").HasConversion<UtcDateTimeConverter>();

        builder.HasOne(h => h.Shipment).WithMany(s => s.History).HasForeignKey(h => h.ShipmentId).OnDelete(DeleteBehavior.Restrict);
    }
}
