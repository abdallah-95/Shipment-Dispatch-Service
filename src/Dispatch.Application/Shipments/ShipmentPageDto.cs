using Dispatch.Domain;

namespace Dispatch.Application.Shipments;

public sealed record ShipmentListItemDto(
    int Id,
    string TrackingNumber,
    int CustomerId,
    string CustomerName,
    int? DriverId,
    string? DriverName,
    string Origin,
    string Destination,
    decimal WeightKg,
    ShipmentStatus Status,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    int Version);

/// <summary>
/// One page of the list. <see cref="PageSize"/> is the effective value (after clamping), and
/// <see cref="TotalCount"/> counts every row matching the same filters.
/// </summary>
public sealed record ShipmentPageDto(
    IReadOnlyList<ShipmentListItemDto> Items,
    int Page,
    int PageSize,
    int TotalCount);
