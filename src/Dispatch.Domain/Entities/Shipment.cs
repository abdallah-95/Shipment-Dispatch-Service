namespace Dispatch.Domain.Entities;

public class Shipment : IVersioned
{
    public int Id { get; set; }
    public required string TrackingNumber { get; set; }
    public int CustomerId { get; set; }
    public int? DriverId { get; set; }
    public required string Origin { get; set; }
    public required string Destination { get; set; }
    public decimal WeightKg { get; set; }
    public ShipmentStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public int Version { get; set; }
    public required string SearchText { get; set; }

    public Customer? Customer { get; set; }
    public Driver? Driver { get; set; }
    public List<ShipmentStatusHistory> History { get; set; } = [];
}
