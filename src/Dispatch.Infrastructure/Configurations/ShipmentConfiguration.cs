using Dispatch.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dispatch.Infrastructure.Configurations;

internal sealed class ShipmentConfiguration : IEntityTypeConfiguration<Shipment>
{
    public void Configure(EntityTypeBuilder<Shipment> builder)
    {
        builder.ToTable("Shipments");

        builder.Property(s => s.TrackingNumber).HasMaxLength(32).IsUnicode(false).IsRequired();
        builder.Property(s => s.Origin).HasMaxLength(100).IsRequired();
        builder.Property(s => s.Destination).HasMaxLength(100).IsRequired();
        builder.Property(s => s.WeightKg).HasPrecision(8, 2);
        builder.Property(s => s.Status).HasConversion<string>().HasMaxLength(20).IsUnicode(false);
        builder.Property(s => s.CreatedAt).HasColumnType("datetime2").HasConversion<UtcDateTimeConverter>();
        builder.Property(s => s.UpdatedAt).HasColumnType("datetime2").HasConversion<UtcDateTimeConverter>();
        builder.Property(s => s.SearchText).HasMaxLength(256).IsRequired();

        // Managed optimistic concurrency (incremented by VersionInterceptor), not rowversion.
        builder.Property(s => s.Version).IsConcurrencyToken();

        // Indexes at this stage: the unique tracking number and the foreign keys (below).
        // Filter and sort indexes are added with the list endpoint, justified by measurement.
        builder.HasIndex(s => s.TrackingNumber).IsUnique();

        builder.HasOne(s => s.Customer).WithMany().HasForeignKey(s => s.CustomerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(s => s.Driver).WithMany().HasForeignKey(s => s.DriverId).OnDelete(DeleteBehavior.Restrict);
    }
}
