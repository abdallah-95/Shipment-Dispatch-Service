namespace Dispatch.Domain;

/// <summary>
/// Declared in workflow order (the order a breadth-first walk of <see cref="ShipmentTransitions"/>
/// visits them). The dashboard lists statuses in this order. Stored and sent as names, never numbers.
/// </summary>
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
