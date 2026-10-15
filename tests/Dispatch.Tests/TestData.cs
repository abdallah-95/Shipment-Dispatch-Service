using Dispatch.Domain;
using Dispatch.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace Dispatch.Tests;

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
