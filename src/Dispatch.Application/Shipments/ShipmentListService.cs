using System.Globalization;
using System.Linq.Expressions;
using Dispatch.Domain;
using Dispatch.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Dispatch.Application.Shipments;

public sealed class ShipmentListService(IDispatchDbContext db) : IShipmentListService
{
    public const int DefaultPageSize = 25;
    public const int MaxPageSize = 100;

    private const string DefaultSortBy = "createdAt";
    private const string DefaultSortDir = "desc";

    private delegate IOrderedQueryable<Shipment> Sorter(IQueryable<Shipment> query, bool descending);

    // Status sorts by the stored name (alphabetical), not by workflow order.
    private static readonly Dictionary<string, Sorter> Sorts = new(StringComparer.OrdinalIgnoreCase)
    {
        ["createdAt"] = (query, descending) => SortBy(query, s => s.CreatedAt, descending),
        ["updatedAt"] = (query, descending) => SortBy(query, s => s.UpdatedAt, descending),
        ["status"] = (query, descending) => SortBy(query, s => s.Status, descending),
        ["trackingNumber"] = (query, descending) => SortBy(query, s => s.TrackingNumber, descending),
        ["customerName"] = (query, descending) => SortBy(query, s => s.Customer!.Name, descending),
        ["destination"] = (query, descending) => SortBy(query, s => s.Destination, descending),
    };

    // An instant needs "Z" or an explicit offset; a value without one is ambiguous and rejected.
    private static readonly string[] OffsetFormats =
        ["yyyy-MM-dd'T'HH:mmzzz", "yyyy-MM-dd'T'HH:mm:sszzz", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz"];

    private static readonly string[] UtcFormats =
        ["yyyy-MM-dd'T'HH:mm'Z'", "yyyy-MM-dd'T'HH:mm:ss'Z'", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'"];

    public async Task<ShipmentListResult> ListAsync(ShipmentListRequest request, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();

        var page = request.Page ?? 1;
        if (page < 1)
        {
            errors["page"] = ["page must be 1 or greater."];
        }

        // Oversized pages are clamped, not rejected.
        var pageSize = Math.Min(request.PageSize ?? DefaultPageSize, MaxPageSize);
        if (pageSize < 1)
        {
            errors["pageSize"] = ["pageSize must be 1 or greater."];
        }

        ShipmentStatus? status = null;
        if (request.Status is not null)
        {
            status = ParseStatus(request.Status);
            if (status is null)
            {
                errors["status"] = [$"status must be one of: {string.Join(", ", Enum.GetNames<ShipmentStatus>())}."];
            }
        }

        var createdFrom = ParseInstant(request.CreatedFrom, "createdFrom", errors);
        var createdTo = ParseInstant(request.CreatedTo, "createdTo", errors);
        if (createdFrom > createdTo)
        {
            errors["createdFrom"] = ["createdFrom must not be after createdTo."];
        }

        var search = ShipmentSearch.Parse(request.Search);
        if (search is SearchCondition.TooLong)
        {
            errors["search"] = [$"search must be at most {ShipmentSearch.MaxLength} characters."];
        }

        if (!Sorts.TryGetValue(request.SortBy ?? DefaultSortBy, out var sort))
        {
            errors["sortBy"] = [$"sortBy must be one of: {string.Join(", ", Sorts.Keys)}."];
        }

        var sortDir = request.SortDir ?? DefaultSortDir;
        var descending = string.Equals(sortDir, "desc", StringComparison.OrdinalIgnoreCase);
        if (!descending && !string.Equals(sortDir, "asc", StringComparison.OrdinalIgnoreCase))
        {
            errors["sortDir"] = ["sortDir must be asc or desc."];
        }

        var offset = (long)(page - 1) * pageSize;
        if (offset > int.MaxValue)
        {
            errors["page"] = ["page is too large for this pageSize."];
        }

        if (errors.Count > 0 || sort is null)
        {
            return new ShipmentListResult.Invalid(errors);
        }

        // Nothing can match a search with no letters or digits, so skip the database entirely.
        if (search is SearchCondition.NoTerms)
        {
            return new ShipmentListResult.Success(new ShipmentPageDto([], page, pageSize, 0));
        }

        // The one filtered query. The count and the page are both derived from it, so their
        // predicates cannot diverge.
        var filtered = (search is SearchCondition.FullText fullText
                ? db.ShipmentsMatching(fullText.Condition)
                : db.Shipments)
            .AsNoTracking();

        if (status is { } wantedStatus)
        {
            filtered = filtered.Where(s => s.Status == wantedStatus);
        }

        // Half-open: [createdFrom, createdTo).
        if (createdFrom is { } from)
        {
            filtered = filtered.Where(s => s.CreatedAt >= from);
        }

        if (createdTo is { } to)
        {
            filtered = filtered.Where(s => s.CreatedAt < to);
        }

        if (request.DriverId is { } driverId)
        {
            filtered = filtered.Where(s => s.DriverId == driverId);
        }

        var totalCount = await filtered.CountAsync(cancellationToken);

        var items = await sort(filtered, descending)
            .Skip((int)offset)
            .Take(pageSize)
            .Select(s => new ShipmentListItemDto(
                s.Id,
                s.TrackingNumber,
                s.CustomerId,
                s.Customer!.Name,
                s.DriverId,
                s.Driver == null ? null : s.Driver.Name,
                s.Origin,
                s.Destination,
                s.WeightKg,
                s.Status,
                s.CreatedAt,
                s.UpdatedAt,
                s.Version))
            .ToListAsync(cancellationToken);

        return new ShipmentListResult.Success(new ShipmentPageDto(items, page, pageSize, totalCount));
    }

    /// <summary>Every sort ends with Id in the same direction, so equal keys always come back in the same order.</summary>
    private static IOrderedQueryable<Shipment> SortBy<TKey>(
        IQueryable<Shipment> query, Expression<Func<Shipment, TKey>> key, bool descending) =>
        descending
            ? query.OrderByDescending(key).ThenByDescending(s => s.Id)
            : query.OrderBy(key).ThenBy(s => s.Id);

    /// <summary>A status name, case-insensitive. Numbers are rejected, as they are in JSON bodies.</summary>
    private static ShipmentStatus? ParseStatus(string value)
    {
        foreach (var candidate in Enum.GetValues<ShipmentStatus>())
        {
            if (string.Equals(candidate.ToString(), value, StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        return null;
    }

    private static DateTime? ParseInstant(string? value, string parameter, Dictionary<string, string[]> errors)
    {
        if (value is null)
        {
            return null;
        }

        if (DateTimeOffset.TryParseExact(value, OffsetFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var instant)
            || DateTimeOffset.TryParseExact(value, UtcFormats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out instant))
        {
            return instant.UtcDateTime;
        }

        errors[parameter] =
        [
            $"{parameter} must be an ISO-8601 instant with Z or an offset, such as 2026-01-31T00:00:00Z. " +
            "Send a '+' offset as %2B in a query string.",
        ];
        return null;
    }
}
