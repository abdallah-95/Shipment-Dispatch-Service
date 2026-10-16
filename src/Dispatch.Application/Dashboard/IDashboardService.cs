namespace Dispatch.Application.Dashboard;

public interface IDashboardService
{
    /// <summary>Counts shipments per status with one GROUP BY in the database.</summary>
    Task<DashboardSummaryDto> GetSummaryAsync(CancellationToken cancellationToken);
}
