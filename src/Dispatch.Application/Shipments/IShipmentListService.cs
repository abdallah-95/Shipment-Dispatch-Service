namespace Dispatch.Application.Shipments;

public interface IShipmentListService
{
    /// <summary>Validates the request, then returns one page of shipments and the total matching count.</summary>
    Task<ShipmentListResult> ListAsync(ShipmentListRequest request, CancellationToken cancellationToken);
}
