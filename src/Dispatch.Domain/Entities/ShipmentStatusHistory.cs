namespace Dispatch.Domain.Entities;

public class ShipmentStatusHistory
{
    public int Id { get; set; }
    public int ShipmentId { get; set; }
    public ShipmentStatus? FromStatus { get; set; }
    public ShipmentStatus ToStatus { get; set; }
    public DateTime ChangedAt { get; set; }

    public Shipment? Shipment { get; set; }
}
