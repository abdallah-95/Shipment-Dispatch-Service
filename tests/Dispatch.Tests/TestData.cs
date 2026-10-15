using Dispatch.Domain;
using Dispatch.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace Dispatch.Tests;

/// <summary>The fields of a list-test shipment that the list endpoint filters, sorts and searches on.</summary>
internal sealed record ListRow(
    ShipmentStatus Status,
    DateTime CreatedAt,
    string CustomerName,
    int? DriverId,
    string Destination = "Alexandria");

/// <summary>Creates each test's own rows. Nothing here reads the seeded dataset.</summary>
internal static class TestData
{
    /// <summary>Base time for inserted history; each step is one hour later.</summary>
    private static readonly DateTime HistoryStart = new(2026, 1, 1, 8, 0, 0, DateTimeKind.Utc);

    /// <summary>"Now" for tests. Later than any inserted history, so new rows keep timestamps ascending.</summary>
    public static FakeTimeProvider CreateClock() => new(new DateTimeOffset(2026, 2, 1, 12, 0, 0, TimeSpan.Zero));

    /// <summary>
    /// Inserts a customer and a shipment already at <paramref name="status"/>, with the complete history
    /// chain, a matching Version and UpdatedAt, and a driver unless it is still Created.
    /// </summary>
    public static async Task<Shipment> CreateShipmentAsync(TestDatabaseFixture database, ShipmentStatus status)
    {
        var unique = Guid.NewGuid().ToString("N");
        var trackingNumber = $"TST{unique}"[..32]; // column is varchar(32)
        var path = PathTo(status);

        var customer = new Customer { Name = $"Status Test Customer {unique}" };
        var shipment = new Shipment
        {
            TrackingNumber = trackingNumber,
            Customer = customer,
            Driver = status == ShipmentStatus.Created ? null : new Driver { Name = $"Status Test Driver {unique}" },
            Origin = "Central Hub",
            Destination = "Alexandria",
            WeightKg = 3.25m,
            Status = status,
            CreatedAt = HistoryStart,
            UpdatedAt = HistoryStart.AddHours(path.Count - 1),
            Version = path.Count - 1,
            SearchText = SearchTextNormalizer.Build(trackingNumber, customer.Name, "Alexandria"),
        };

        for (var i = 0; i < path.Count; i++)
        {
            shipment.History.Add(new ShipmentStatusHistory
            {
                FromStatus = i == 0 ? null : path[i - 1],
                ToStatus = path[i],
                ChangedAt = HistoryStart.AddHours(i),
            });
        }

        await using var db = database.CreateDbContext();
        db.Shipments.Add(shipment);
        await db.SaveChangesAsync();
        return shipment;
    }

    /// <summary>
    /// Inserts shipments for list tests, each with the complete history chain to its status. Rows that
    /// share a customer name share one customer. <see cref="ListRow.DriverId"/> must name a saved driver
    /// for every status except Created, as it would in real data.
    /// </summary>
    public static async Task<List<Shipment>> CreateListShipmentsAsync(TestDatabaseFixture database, IEnumerable<ListRow> rows)
    {
        var customers = new Dictionary<string, Customer>();
        var shipments = new List<Shipment>();

        foreach (var row in rows)
        {
            if ((row.Status == ShipmentStatus.Created) != (row.DriverId is null))
            {
                throw new ArgumentException("A shipment has a driver exactly when it is past Created.", nameof(rows));
            }

            if (!customers.TryGetValue(row.CustomerName, out var customer))
            {
                customer = new Customer { Name = row.CustomerName };
                customers.Add(row.CustomerName, customer);
            }

            var trackingNumber = $"TST{Guid.NewGuid():N}"[..32]; // column is varchar(32)
            var path = PathTo(row.Status);
            var shipment = new Shipment
            {
                TrackingNumber = trackingNumber,
                Customer = customer,
                DriverId = row.DriverId,
                Origin = "Central Hub",
                Destination = row.Destination,
                WeightKg = 1.5m,
                Status = row.Status,
                CreatedAt = row.CreatedAt,
                UpdatedAt = row.CreatedAt.AddHours(path.Count - 1),
                Version = path.Count - 1,
                SearchText = SearchTextNormalizer.Build(trackingNumber, row.CustomerName, row.Destination),
            };

            for (var i = 0; i < path.Count; i++)
            {
                shipment.History.Add(new ShipmentStatusHistory
                {
                    FromStatus = i == 0 ? null : path[i - 1],
                    ToStatus = path[i],
                    ChangedAt = row.CreatedAt.AddHours(i),
                });
            }

            shipments.Add(shipment);
        }

        await using var db = database.CreateDbContext();
        db.Shipments.AddRange(shipments);
        await db.SaveChangesAsync();
        return shipments;
    }

    public static async Task<Driver> CreateDriverAsync(TestDatabaseFixture database)
    {
        var driver = new Driver { Name = $"Status Test Driver {Guid.NewGuid():N}" };
        await using var db = database.CreateDbContext();
        db.Drivers.Add(driver);
        await db.SaveChangesAsync();
        return driver;
    }

    public static async Task<Shipment> LoadShipmentAsync(TestDatabaseFixture database, int shipmentId)
    {
        await using var db = database.CreateDbContext();
        return await db.Shipments.AsNoTracking().SingleAsync(s => s.Id == shipmentId);
    }

    /// <summary>History rows for the shipment in insertion order.</summary>
    public static async Task<List<ShipmentStatusHistory>> LoadHistoryAsync(TestDatabaseFixture database, int shipmentId)
    {
        await using var db = database.CreateDbContext();
        return await db.ShipmentStatusHistory
            .AsNoTracking()
            .Where(h => h.ShipmentId == shipmentId)
            .OrderBy(h => h.Id)
            .ToListAsync();
    }

    /// <summary>Shortest legal path from Created to <paramref name="target"/>, walked over the transition map.</summary>
    private static List<ShipmentStatus> PathTo(ShipmentStatus target)
    {
        var previous = new Dictionary<ShipmentStatus, ShipmentStatus>();
        var queue = new Queue<ShipmentStatus>([ShipmentStatus.Created]);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (current == target)
            {
                var path = new List<ShipmentStatus> { current };
                while (previous.TryGetValue(path[0], out var before))
                {
                    path.Insert(0, before);
                }

                return path;
            }

            foreach (var next in ShipmentTransitions.AllowedFrom(current))
            {
                if (next != ShipmentStatus.Created && previous.TryAdd(next, current))
                {
                    queue.Enqueue(next);
                }
            }
        }

        throw new InvalidOperationException($"{target} is not reachable from {ShipmentStatus.Created}.");
    }
}
