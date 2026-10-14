using Dispatch.Application.Shipments;
using Microsoft.AspNetCore.Mvc;

namespace Dispatch.Api.Controllers;

[ApiController]
[Route("api/shipments")]
public sealed class ShipmentsController(IShipmentQueryService shipments) : ControllerBase
{
    [HttpGet("{id:int}")]
    [ProducesResponseType<ShipmentDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ShipmentDetailDto>> GetById(int id, CancellationToken cancellationToken)
    {
        var detail = await shipments.GetDetailAsync(id, cancellationToken);

        if (detail is null)
        {
            return Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Shipment not found",
                detail: $"No shipment with id {id} exists.");
        }

        return detail;
    }
}
