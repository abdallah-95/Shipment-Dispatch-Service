using Dispatch.Domain;

namespace Dispatch.Application.Shipments;

public interface IShipmentStatusService
{
    /// <summary>
    /// Moves a shipment to <paramref name="targetStatus"/> if the client's version is current and the
    /// transition is legal. The status, driver, timestamp and history row are saved together.
    /// </summary>
    Task<ChangeStatusResult> ChangeStatusAsync(
        int shipmentId,
        ShipmentStatus targetStatus,
        int expectedVersion,
        int? driverId,
        CancellationToken cancellationToken);
}
