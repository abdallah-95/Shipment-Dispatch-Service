using Dispatch.Application;
using Dispatch.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Dispatch.Infrastructure;

/// <summary>
/// Save-time behaviour belongs in SaveChangesInterceptors, never in overrides here. The only
/// override is model building, which loads the per-entity configurations.
/// </summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IDispatchDbContext
{
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Driver> Drivers => Set<Driver>();
    public DbSet<Shipment> Shipments => Set<Shipment>();
    public DbSet<ShipmentStatusHistory> ShipmentStatusHistory => Set<ShipmentStatusHistory>();

    public IQueryable<Shipment> ShipmentsMatching(string containsCondition) =>
        Shipments.Where(s => EF.Functions.Contains(s.SearchText, containsCondition));

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
}
