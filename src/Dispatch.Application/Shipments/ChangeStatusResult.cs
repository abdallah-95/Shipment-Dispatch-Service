using Dispatch.Domain;

namespace Dispatch.Application.Shipments;

/// <summary>
/// Every outcome of a status change. Expected failures are results, not exceptions; the
/// controller maps each case to an HTTP response.
/// </summary>
public abstract record ChangeStatusResult
{
    private ChangeStatusResult()
    {
    }

    public sealed record Success(ShipmentStatusChangedDto Shipment) : ChangeStatusResult;

    public sealed record NotFound : ChangeStatusResult;

    /// <summary>The shipment is not at the version the client expected. Carries its current state.</summary>
    public sealed record VersionConflict(
        ShipmentStatus CurrentStatus,
        int CurrentVersion,
        IReadOnlyList<ShipmentStatus> AllowedTransitions) : ChangeStatusResult;

    /// <summary>The requested status is not reachable from the current one.</summary>
    public sealed record IllegalTransition(
        ShipmentStatus CurrentStatus,
        ShipmentStatus RequestedStatus,
        IReadOnlyList<ShipmentStatus> AllowedTransitions) : ChangeStatusResult;

    /// <summary>Moving to PickedUp without a driverId that names an existing driver.</summary>
    public sealed record DriverRequired(int? DriverId) : ChangeStatusResult;

    /// <summary>The request is malformed for this transition.</summary>
    public sealed record Invalid(string Detail) : ChangeStatusResult;
}
