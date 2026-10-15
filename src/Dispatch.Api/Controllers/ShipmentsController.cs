using Dispatch.Application.Shipments;
using Microsoft.AspNetCore.Mvc;

namespace Dispatch.Api.Controllers;

[ApiController]
[Route("api/shipments")]
public sealed class ShipmentsController(IShipmentQueryService shipments, IShipmentStatusService statuses) : ControllerBase
{
    /// <summary>
    /// Gets one shipment with its customer, driver and full status history.
    /// </summary>
    /// <param name="id">The shipment id.</param>
    /// <response code="200">The shipment. Its <c>version</c> is what a status change sends back as <c>expectedVersion</c>.</response>
    /// <response code="404">No shipment with this id.</response>
    [HttpGet("{id:int}")]
    [ProducesResponseType<ShipmentDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ShipmentDetailDto>> GetById(int id, CancellationToken cancellationToken)
    {
        var detail = await shipments.GetDetailAsync(id, cancellationToken);

        if (detail is null)
        {
            return ShipmentNotFound(id);
        }

        return detail;
    }

    /// <summary>
    /// Moves a shipment to the next status and records it in the status history.
    /// </summary>
    /// <remarks>
    /// <c>expectedVersion</c> is the <c>version</c> from the last GET or POST response for this shipment;
    /// <c>driverId</c> is required only when moving to PickedUp (and rejected for any other status).
    /// </remarks>
    /// <param name="id">The shipment id.</param>
    /// <param name="request">The target status, the version the client last saw, and a driver when picking up.</param>
    /// <response code="200">The shipment's new status and version.</response>
    /// <response code="400">The body is invalid: a required field is missing, the status is not a status name, or <c>driverId</c> was sent for a status other than PickedUp.</response>
    /// <response code="404">No shipment with this id.</response>
    [HttpPost("{id:int}/status")]
    [ProducesResponseType<ShipmentStatusChangedDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ShipmentStatusChangedDto>> ChangeStatus(
        int id,
        ChangeShipmentStatusRequest request,
        CancellationToken cancellationToken)
    {
        // [ApiController] has already returned 400 if either required field is missing.
        var result = await statuses.ChangeStatusAsync(
            id, request.Status!.Value, request.ExpectedVersion!.Value, request.DriverId, cancellationToken);

        return result switch
        {
            ChangeStatusResult.Success success => Ok(success.Shipment),

            ChangeStatusResult.NotFound => ShipmentNotFound(id),

            ChangeStatusResult.VersionConflict conflict => ProblemWithExtensions(
                StatusCodes.Status409Conflict,
                "Version conflict",
                $"Shipment {id} is at version {conflict.CurrentVersion}, not {request.ExpectedVersion}. Re-read it and retry.",
                new Dictionary<string, object?>
                {
                    ["currentStatus"] = conflict.CurrentStatus,
                    ["currentVersion"] = conflict.CurrentVersion,
                    ["allowedTransitions"] = conflict.AllowedTransitions,
                }),

            ChangeStatusResult.IllegalTransition illegal => ProblemWithExtensions(
                StatusCodes.Status422UnprocessableEntity,
                "Illegal status transition",
                $"Shipment {id} cannot move from {illegal.CurrentStatus} to {illegal.RequestedStatus}.",
                new Dictionary<string, object?>
                {
                    ["currentStatus"] = illegal.CurrentStatus,
                    ["allowedTransitions"] = illegal.AllowedTransitions,
                }),

            ChangeStatusResult.DriverRequired driver => Problem(
                statusCode: StatusCodes.Status422UnprocessableEntity,
                title: "Driver required",
                detail: driver.DriverId is null
                    ? "Moving to PickedUp requires a driverId."
                    : $"No driver with id {driver.DriverId} exists."),

            ChangeStatusResult.Invalid invalid => Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid request",
                detail: invalid.Detail),

            _ => throw new InvalidOperationException($"Unhandled result {result.GetType().Name}."),
        };
    }

    private ObjectResult ShipmentNotFound(int id) =>
        Problem(
            statusCode: StatusCodes.Status404NotFound,
            title: "Shipment not found",
            detail: $"No shipment with id {id} exists.");

    /// <summary>
    /// <see cref="ControllerBase.Problem"/> has no extensions parameter in .NET 8, so this builds the
    /// ProblemDetails through the same factory (keeping traceId and type defaults) and adds them.
    /// </summary>
    private ObjectResult ProblemWithExtensions(
        int statusCode, string title, string detail, IReadOnlyDictionary<string, object?> extensions)
    {
        var problem = ProblemDetailsFactory.CreateProblemDetails(HttpContext, statusCode, title, detail: detail);
        foreach (var (key, value) in extensions)
        {
            problem.Extensions[key] = value;
        }

        return new ObjectResult(problem) { StatusCode = statusCode };
    }
}
