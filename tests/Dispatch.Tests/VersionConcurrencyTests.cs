using Dispatch.Domain;
using Dispatch.Domain.Entities;
using Dispatch.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace Dispatch.Tests;

/// <summary>
/// The Version token and VersionInterceptor at the DbContext level, with no HTTP in between, so the
/// interleaving is fully under the test's control.
/// </summary>
[Collection(TestDatabaseCollection.Name)]
public sealed class VersionConcurrencyTests(TestDatabaseFixture database)
{
    private readonly FakeTimeProvider _clock = TestData.CreateClock();

    [Fact]
    public async Task Two_contexts_at_the_same_version_first_save_wins_second_throws_one_history_row()
    {
        var shipment = await TestData.CreateShipmentAsync(database, ShipmentStatus.PickedUp);
        var historyBefore = await TestData.LoadHistoryAsync(database, shipment.Id);

        await using var first = database.CreateDbContext();
        await using var second = database.CreateDbContext();
        var firstCopy = await first.Shipments.SingleAsync(s => s.Id == shipment.Id);
        var secondCopy = await second.Shipments.SingleAsync(s => s.Id == shipment.Id);
        Assert.Equal(firstCopy.Version, secondCopy.Version);

        AdvanceTo(first, firstCopy, ShipmentStatus.InTransit);
        AdvanceTo(second, secondCopy, ShipmentStatus.InTransit);

        await first.SaveChangesAsync();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());

        var stored = await TestData.LoadShipmentAsync(database, shipment.Id);
        Assert.Equal(ShipmentStatus.InTransit, stored.Status);
        Assert.Equal(shipment.Version + 1, stored.Version);
        var historyAfter = await TestData.LoadHistoryAsync(database, shipment.Id);
        Assert.Single(historyAfter.Skip(historyBefore.Count));
    }

    [Fact]
    public async Task Synchronous_SaveChanges_also_increments_Version()
    {
        var shipment = await TestData.CreateShipmentAsync(database, ShipmentStatus.PickedUp);

        await using (var db = database.CreateDbContext())
        {
            var tracked = await db.Shipments.SingleAsync(s => s.Id == shipment.Id);
            AdvanceTo(db, tracked, ShipmentStatus.InTransit);
            db.SaveChanges(); // the sync path goes through SavingChanges, not SavingChangesAsync
        }

        var stored = await TestData.LoadShipmentAsync(database, shipment.Id);
        Assert.Equal(shipment.Version + 1, stored.Version);
    }

    /// <summary>The same writes the status handler makes: status, UpdatedAt and one history row.</summary>
    private void AdvanceTo(AppDbContext db, Shipment shipment, ShipmentStatus to)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        db.ShipmentStatusHistory.Add(new ShipmentStatusHistory
        {
            ShipmentId = shipment.Id,
            FromStatus = shipment.Status,
            ToStatus = to,
            ChangedAt = now,
        });
        shipment.Status = to;
        shipment.UpdatedAt = now;
    }
}
