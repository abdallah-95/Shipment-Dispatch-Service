namespace Dispatch.Domain;

public enum ShipmentStatus
{
    Created,
    PickedUp,
    InTransit,
    OutForDelivery,
    Delivered,
    Failed,
    Returned,
}
