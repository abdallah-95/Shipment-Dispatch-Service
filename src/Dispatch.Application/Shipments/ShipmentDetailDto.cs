using Dispatch.Domain;

namespace Dispatch.Application.Shipments;

public sealed record PartyDto(int Id, string Name);

public sealed record StatusHistoryDto(int Id, ShipmentStatus? FromStatus, ShipmentStatus ToStatus, DateTime ChangedAt);

/// <summary>
/// <see cref="Version"/> is what a client sends back as <c>expectedVersion</c> when changing status.
/// </summary>
public sealed record ShipmentDetailDto(
    int Id,
    string TrackingNumber,
    ShipmentStatus Status,
    int Version,
    string Origin,
    string Destination,
    decimal WeightKg,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    PartyDto Customer,
    PartyDto? Driver,
    IReadOnlyList<StatusHistoryDto> History);
