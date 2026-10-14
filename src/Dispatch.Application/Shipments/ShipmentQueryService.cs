using Microsoft.EntityFrameworkCore;

namespace Dispatch.Application.Shipments;

public sealed class ShipmentQueryService(IDispatchDbContext db) : IShipmentQueryService
{
    public Task<ShipmentDetailDto?> GetDetailAsync(int id, CancellationToken cancellationToken) =>
        db.Shipments
            .AsNoTracking()
            .Where(s => s.Id == id)
            .Select(s => new ShipmentDetailDto(
                s.Id,
                s.TrackingNumber,
                s.Status,
                s.Version,
                s.Origin,
                s.Destination,
                s.WeightKg,
                s.CreatedAt,
                s.UpdatedAt,
                new PartyDto(s.Customer!.Id, s.Customer.Name),
                s.Driver == null ? null : new PartyDto(s.Driver.Id, s.Driver.Name),
                s.History
                    .OrderBy(h => h.ChangedAt)
                    .ThenBy(h => h.Id)
                    .Select(h => new StatusHistoryDto(h.Id, h.FromStatus, h.ToStatus, h.ChangedAt))
                    .ToList()))
            .SingleOrDefaultAsync(cancellationToken);
}
