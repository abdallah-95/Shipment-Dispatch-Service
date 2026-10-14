namespace Dispatch.Application.Shipments;

public interface IShipmentQueryService
{
    /// <summary>Returns the shipment with customer, driver and full status history, or null if it does not exist.</summary>
    Task<ShipmentDetailDto?> GetDetailAsync(int id, CancellationToken cancellationToken);
}
