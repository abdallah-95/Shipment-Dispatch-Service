using System.ComponentModel.DataAnnotations;
using Dispatch.Domain;

namespace Dispatch.Application.Shipments;

/// <summary>
/// Body of <c>POST /api/shipments/{id}/status</c>. The required fields are nullable on purpose:
/// a missing value stays null and fails validation with 400, instead of silently binding as 0
/// (or as <see cref="ShipmentStatus.Created"/>).
/// </summary>
public sealed record ChangeShipmentStatusRequest
{
    [Required]
    public ShipmentStatus? Status { get; init; }

    /// <summary>The <c>version</c> the client last read. A mismatch is a 409.</summary>
    [Required]
    public int? ExpectedVersion { get; init; }

    /// <summary>Required when moving to PickedUp; rejected with any other transition.</summary>
    public int? DriverId { get; init; }
}
