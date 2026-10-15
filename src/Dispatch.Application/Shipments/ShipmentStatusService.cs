using Dispatch.Domain;
using Dispatch.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Dispatch.Application.Shipments;

public sealed class ShipmentStatusService(IDispatchDbContext db, TimeProvider timeProvider) : IShipmentStatusService
{
    public async Task<ChangeStatusResult> ChangeStatusAsync(
        int shipmentId,
        ShipmentStatus targetStatus,
        int expectedVersion,
        int? driverId,
        CancellationToken cancellationToken)
    {
        // The JSON converter already rejects unknown names and numbers; this guards any other caller.
        if (!Enum.IsDefined(targetStatus))
        {
            return new ChangeStatusResult.Invalid($"'{targetStatus}' is not a shipment status.");
        }

        var shipment = await db.Shipments.SingleOrDefaultAsync(s => s.Id == shipmentId, cancellationToken);
        if (shipment is null)
        {
            return new ChangeStatusResult.NotFound();
        }

        // Version before transition: a stale client gets the current state, not a complaint about a
        // transition it would not have asked for had it seen that state.
        if (shipment.Version != expectedVersion)
        {
            return Conflict(shipment.Status, shipment.Version);
        }

        if (!ShipmentTransitions.IsAllowed(shipment.Status, targetStatus))
        {
            return new ChangeStatusResult.IllegalTransition(
                shipment.Status, targetStatus, ShipmentTransitions.AllowedFrom(shipment.Status));
        }

        // A driver is assigned exactly once, at pickup, so DriverId is null only while Created.
        if (targetStatus == ShipmentStatus.PickedUp)
        {
            if (driverId is null || !await db.Drivers.AnyAsync(d => d.Id == driverId, cancellationToken))
            {
                return new ChangeStatusResult.DriverRequired(driverId);
            }
        }
        else if (driverId is not null)
        {
            return new ChangeStatusResult.Invalid(
                $"driverId can only be set when moving to {ShipmentStatus.PickedUp}.");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var fromStatus = shipment.Status;

        shipment.Status = targetStatus;
        if (targetStatus == ShipmentStatus.PickedUp)
        {
            shipment.DriverId = driverId;
        }

        shipment.UpdatedAt = now;
        db.ShipmentStatusHistory.Add(new ShipmentStatusHistory
        {
            ShipmentId = shipment.Id,
            FromStatus = fromStatus,
            ToStatus = targetStatus,
            ChangedAt = now,
        });

        // One save: the shipment UPDATE (checked against the loaded Version) and the history INSERT
        // commit together or not at all. VersionInterceptor sets the new Version.
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another writer changed (or removed) the row between our load and save. The tracked
            // entity now holds our rejected edits, not the database's state, so discard it (nothing
            // can then re-save it: no retry) and re-read what is actually there.
            db.ChangeTracker.Clear();

            var current = await db.Shipments
                .AsNoTracking()
                .Where(s => s.Id == shipmentId)
                .Select(s => new { s.Status, s.Version })
                .SingleOrDefaultAsync(cancellationToken);

            return current is null
                ? new ChangeStatusResult.NotFound()
                : Conflict(current.Status, current.Version);
        }

        return new ChangeStatusResult.Success(new ShipmentStatusChangedDto(
            shipment.Id, shipment.Status, shipment.DriverId, shipment.UpdatedAt, shipment.Version));
    }

    private static ChangeStatusResult.VersionConflict Conflict(ShipmentStatus status, int version) =>
        new(status, version, ShipmentTransitions.AllowedFrom(status));
}
