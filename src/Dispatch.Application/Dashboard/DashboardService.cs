using Dispatch.Domain;
using Microsoft.EntityFrameworkCore;

namespace Dispatch.Application.Dashboard;

public sealed class DashboardService(IDispatchDbContext db) : IDashboardService
{
    public async Task<DashboardSummaryDto> GetSummaryAsync(CancellationToken cancellationToken)
    {
        // One statement: SELECT Status, COUNT(*) ... GROUP BY Status. No shipment rows leave the database.
        var counts = await db.Shipments
            .GroupBy(s => s.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Status, g => g.Count, cancellationToken);

        // Every status in workflow order (the enum's declaration order), zero-filled.
        var byStatus = Enum.GetValues<ShipmentStatus>()
            .Select(status => new StatusCountDto(status, counts.GetValueOrDefault(status)))
            .ToList();

        // The sum of the same rows rather than a second COUNT(*), so total always equals the sum of byStatus.
        return new DashboardSummaryDto(byStatus.Sum(s => s.Count), byStatus);
    }
}
