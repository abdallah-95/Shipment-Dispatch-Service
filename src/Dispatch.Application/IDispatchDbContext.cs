using Dispatch.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Dispatch.Application;

/// <summary>
/// The slice of the DbContext that Application services use. It exists only to keep the
/// dependency direction inward (Infrastructure references Application, not the reverse);
/// it is not a repository. <c>AppDbContext</c> implements it.
/// </summary>
public interface IDispatchDbContext
{
    DbSet<Customer> Customers { get; }
    DbSet<Driver> Drivers { get; }
    DbSet<Shipment> Shipments { get; }
    DbSet<ShipmentStatusHistory> ShipmentStatusHistory { get; }
}
