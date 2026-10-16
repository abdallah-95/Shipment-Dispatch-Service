using Dispatch.Application.Dashboard;
using Microsoft.AspNetCore.Mvc;

namespace Dispatch.Api.Controllers;

[ApiController]
[Route("api/dashboard")]
public sealed class DashboardController(IDashboardService dashboard) : ControllerBase
{
    /// <summary>
    /// Counts shipments by status.
    /// </summary>
    /// <remarks>
    /// <c>byStatus</c> always lists all seven statuses in workflow order (Created, PickedUp, InTransit,
    /// OutForDelivery, Delivered, Failed, Returned), with 0 for a status no shipment is in.
    /// <c>total</c> is the sum of those counts. Counted live on every request; nothing is cached.
    /// </remarks>
    /// <response code="200">The total and the count for each status.</response>
    [HttpGet("summary")]
    [ProducesResponseType<DashboardSummaryDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<DashboardSummaryDto>> Summary(CancellationToken cancellationToken) =>
        await dashboard.GetSummaryAsync(cancellationToken);
}
