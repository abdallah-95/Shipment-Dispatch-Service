namespace Dispatch.Application.Shipments;

/// <summary>
/// The raw query-string values of <c>GET /api/shipments</c>. Enum-like values and dates arrive as
/// strings so the service, not the model binder, decides what is valid: names only (no numbers), and
/// instants only (an offset is required).
/// </summary>
public sealed record ShipmentListRequest(
    int? Page,
    int? PageSize,
    string? Status,
    string? CreatedFrom,
    string? CreatedTo,
    int? DriverId,
    string? Search,
    string? SortBy,
    string? SortDir);
