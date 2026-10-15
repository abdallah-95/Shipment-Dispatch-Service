using Dispatch.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

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

    /// <summary>
    /// Shipments whose <c>SearchText</c> matches a full-text CONTAINS condition (see
    /// <see cref="Shipments.ShipmentSearch"/>). It lives here because <c>EF.Functions.Contains</c> is part
    /// of the SQL Server provider, which Application does not reference. The condition is sent as a
    /// parameter, and the result is an ordinary query that callers keep composing.
    /// </summary>
    IQueryable<Shipment> ShipmentsMatching(string containsCondition);

    ChangeTracker ChangeTracker { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
