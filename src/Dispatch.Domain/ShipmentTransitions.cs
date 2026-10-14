using System.Collections.Frozen;

namespace Dispatch.Domain;

/// <summary>
/// The status state machine as data. This single map is the source for transition
/// validation and for the <c>allowedTransitions</c> field of 409 responses.
/// </summary>
public static class ShipmentTransitions
{
    public static readonly FrozenDictionary<ShipmentStatus, IReadOnlyList<ShipmentStatus>> Map =
        new Dictionary<ShipmentStatus, IReadOnlyList<ShipmentStatus>>
        {
            [ShipmentStatus.Created] = [ShipmentStatus.PickedUp],
            [ShipmentStatus.PickedUp] = [ShipmentStatus.InTransit],
            [ShipmentStatus.InTransit] = [ShipmentStatus.OutForDelivery],
            [ShipmentStatus.OutForDelivery] = [ShipmentStatus.Delivered, ShipmentStatus.Failed],
            [ShipmentStatus.Delivered] = [ShipmentStatus.Returned],
            [ShipmentStatus.Failed] = [],
            [ShipmentStatus.Returned] = [],
        }.ToFrozenDictionary();

    public static IReadOnlyList<ShipmentStatus> AllowedFrom(ShipmentStatus current) => Map[current];

    public static bool IsAllowed(ShipmentStatus from, ShipmentStatus to) => Map[from].Contains(to);
}
