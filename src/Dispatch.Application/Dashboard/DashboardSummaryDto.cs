using Dispatch.Domain;

namespace Dispatch.Application.Dashboard;

public sealed record StatusCountDto(ShipmentStatus Status, int Count);

/// <summary>
/// <see cref="ByStatus"/> has every status in workflow order, with 0 for a status no shipment is in.
/// <see cref="Total"/> is the sum of those counts.
/// </summary>
public sealed record DashboardSummaryDto(int Total, IReadOnlyList<StatusCountDto> ByStatus);
