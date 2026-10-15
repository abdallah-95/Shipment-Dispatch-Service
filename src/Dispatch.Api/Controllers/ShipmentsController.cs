using Dispatch.Application.Shipments;
using Microsoft.AspNetCore.Mvc;

namespace Dispatch.Api.Controllers;

[ApiController]
[Route("api/shipments")]
public sealed class ShipmentsController(
    IShipmentQueryService shipments,
    IShipmentListService list,
    IShipmentStatusService statuses) : ControllerBase
{
    /// <summary>
    /// Lists shipments one page at a time, with optional filters, search and sort.
    /// </summary>
    /// <remarks>
    /// Filters combine with AND. <c>totalCount</c> counts every shipment matching the same filters.
    /// <c>search</c> is word-prefix matching, not substring matching: "port" finds "Port Said" and
    /// "Portsmouth", but "ort" finds neither.
    /// </remarks>
    /// <param name="page">1-based page number. Default 1. Must be 1 or greater.</param>
    /// <param name="pageSize">Rows per page. Default 25. Must be 1 or greater; values above 100 are clamped to 100.</param>
    /// <param name="status">Only shipments in this status (case-insensitive name): Created, PickedUp, InTransit, OutForDelivery, Delivered, Failed or Returned.</param>
    /// <param name="createdFrom">Only shipments created at or after this instant. ISO-8601 with Z or an offset, such as 2026-09-01T00:00:00Z; send a '+' offset as %2B.</param>
    /// <param name="createdTo">Only shipments created before this instant (exclusive), same format. Must not be earlier than createdFrom.</param>
    /// <param name="driverId">Only shipments assigned to this driver.</param>
    /// <param name="search">Words to find across tracking number, customer name and destination. Each word must match the start of a word (word prefix), not an arbitrary substring, and all words must match. Anything that is not a letter or digit separates words, so quotes, hyphens and operators such as AND/OR/NOT are not syntax. At most 100 characters; only the first 8 words are used. Input with no letters or digits returns an empty page.</param>
    /// <param name="sortBy">createdAt (default), updatedAt, status, trackingNumber, customerName or destination. status sorts by name, alphabetically. Ties are always broken by id, in the same direction.</param>
    /// <param name="sortDir">asc or desc (default).</param>
    /// <response code="200">The page of shipments, the page number, the effective page size and the total count.</response>
    /// <response code="400">A parameter is invalid: page or pageSize below 1, an unknown status, sortBy or sortDir, a date that is not an instant with an offset, createdFrom after createdTo, or search longer than 100 characters. <c>errors</c> is keyed by parameter name.</response>
    [HttpGet]
    [ProducesResponseType<ShipmentPageDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ShipmentPageDto>> List(
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] string? status,
        [FromQuery] string? createdFrom,
        [FromQuery] string? createdTo,
        [FromQuery] int? driverId,
        [FromQuery] string? search,
        [FromQuery] string? sortBy,
        [FromQuery] string? sortDir,
        CancellationToken cancellationToken)
    {
        var result = await list.ListAsync(
            new ShipmentListRequest(page, pageSize, status, createdFrom, createdTo, driverId, search, sortBy, sortDir),
            cancellationToken);

        switch (result)
        {
            case ShipmentListResult.Success success:
                return success.Page;

            case ShipmentListResult.Invalid invalid:
                // Same ValidationProblemDetails shape as a binding failure ([ApiController]).
                foreach (var (parameter, messages) in invalid.Errors)
                {
                    foreach (var message in messages)
                    {
                        ModelState.AddModelError(parameter, message);
                    }
                }

                return ValidationProblem(ModelState);

            default:
                throw new InvalidOperationException($"Unhandled result {result.GetType().Name}.");
        }
    }

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
