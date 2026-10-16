using Dispatch.Domain;
using Dispatch.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Dispatch.Application.Shipments;

/// <remarks>
/// Business log lines identify the shipment by ShipmentId only, never by tracking number or customer.
/// </remarks>
public sealed class ShipmentStatusService(
    IDispatchDbContext db,
    TimeProvider timeProvider,
    ILogger<ShipmentStatusService> logger) : IShipmentStatusService
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
            return Conflict(shipmentId, expectedVersion, shipment.Status, shipment.Version);
        }

        if (!ShipmentTransitions.IsAllowed(shipment.Status, targetStatus))
        {
            logger.LogInformation(
                "Illegal status transition for shipment {ShipmentId} from {FromStatus} to {RequestedStatus}",
                shipmentId, shipment.Status, targetStatus);

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
                : Conflict(shipmentId, expectedVersion, current.Status, current.Version);
        }

        logger.LogInformation(
            "Shipment {ShipmentId} status changed from {FromStatus} to {ToStatus}, new version {NewVersion}",
            shipment.Id, fromStatus, shipment.Status, shipment.Version);

        return new ChangeStatusResult.Success(new ShipmentStatusChangedDto(
            shipment.Id, shipment.Status, shipment.DriverId, shipment.UpdatedAt, shipment.Version));
    }

    /// <summary>Both conflict paths: the version check after load, and losing the race at save.</summary>
    private ChangeStatusResult.VersionConflict Conflict(
        int shipmentId, int expectedVersion, ShipmentStatus currentStatus, int currentVersion)
    {
        logger.LogWarning(
            "Version conflict on shipment {ShipmentId}: expected version {ExpectedVersion}, current version {CurrentVersion}",
            shipmentId, expectedVersion, currentVersion);

        return new(currentStatus, currentVersion, ShipmentTransitions.AllowedFrom(currentStatus));
    }
}
