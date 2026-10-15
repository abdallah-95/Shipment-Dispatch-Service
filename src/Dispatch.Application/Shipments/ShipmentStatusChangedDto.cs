using Dispatch.Domain;

namespace Dispatch.Application.Shipments;

/// <summary>
/// <see cref="Version"/> is the new version, so a client can send it as <c>expectedVersion</c>
/// on its next status change without re-reading the shipment.
/// </summary>
public sealed record ShipmentStatusChangedDto(
    int Id,
    ShipmentStatus Status,
    int? DriverId,
    DateTime UpdatedAt,
    int Version);
