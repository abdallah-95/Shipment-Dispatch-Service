using System.Data.Common;
using System.Diagnostics;
using Dispatch.Application.Shipments;
using Dispatch.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Dispatch.Seeder;

/// <summary>
/// Holds the seeder (and so the API, which starts only after the seeder exits 0) until full-text search
/// covers every shipment, then proves it with a real search.
/// </summary>
internal static class SearchReadiness
{
    // Created by the AddShipmentFullTextSearch migration.
    private const string CatalogName = "ShipmentSearch";

    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan Heartbeat = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Waits until the catalog is idle AND holds every shipment. PopulateStatus alone is not enough: it can
    /// read 0 (idle) before the background crawl of freshly inserted rows has started. Logs each change in
    /// the observed values (and a heartbeat while nothing changes), so the log is the population timeline.
    /// </summary>
    public static async Task<bool> WaitForPopulationAsync(
        DbConnection connection, int expectedItems, ILogger logger, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        (int PopulateStatus, int ItemCount)? lastLogged = null;
        var lastLoggedAt = TimeSpan.Zero;

        while (true)
        {
            var current = await ReadCatalogAsync(connection, cancellationToken);
            var elapsed = stopwatch.Elapsed;

            if (current != lastLogged || elapsed - lastLoggedAt >= Heartbeat)
            {
                logger.LogInformation(
                    "Full-text catalog {Catalog}: PopulateStatus {PopulateStatus}, ItemCount {ItemCount} of {ExpectedItems} at {ElapsedMs} ms",
                    CatalogName, current.PopulateStatus, current.ItemCount, expectedItems, (long)elapsed.TotalMilliseconds);
                lastLogged = current;
                lastLoggedAt = elapsed;
            }

            if (current.PopulateStatus == 0 && current.ItemCount == expectedItems)
            {
                logger.LogInformation(
                    "Full-text population complete: {ItemCount} items, waited {ElapsedMs} ms",
                    current.ItemCount, (long)elapsed.TotalMilliseconds);
                return true;
            }

            if (elapsed >= Timeout)
            {
                logger.LogError(
                    "Full-text population did not complete within {TimeoutSeconds} s: PopulateStatus {PopulateStatus}, ItemCount {ItemCount} of {ExpectedItems}",
                    (int)Timeout.TotalSeconds, current.PopulateStatus, current.ItemCount, expectedItems);
                return false;
            }

            await Task.Delay(PollInterval, cancellationToken);
        }
    }

    /// <summary>
    /// Searches for the newest shipment's tracking number through the same query path the API uses
    /// (<see cref="ShipmentSearch"/> and <c>ShipmentsMatching</c>). No rows is a failure.
    /// </summary>
    public static async Task<bool> SmokeSearchAsync(AppDbContext db, ILogger logger, CancellationToken cancellationToken)
    {
        var token = await db.Shipments
            .OrderByDescending(s => s.Id)
            .Select(s => s.TrackingNumber)
            .FirstAsync(cancellationToken);

        if (ShipmentSearch.Parse(token) is not SearchCondition.FullText search)
        {
            logger.LogError("Smoke search token {Token} produced no search terms", token);
            return false;
        }

        var matches = await db.ShipmentsMatching(search.Condition).CountAsync(cancellationToken);
        if (matches == 0)
        {
            logger.LogError("Smoke search for {Token} ({Condition}) returned no rows", token, search.Condition);
            return false;
        }

        logger.LogInformation("Smoke search for {Token} ({Condition}) returned {Matches} rows", token, search.Condition, matches);
        return true;
    }

    private static async Task<(int PopulateStatus, int ItemCount)> ReadCatalogAsync(
        DbConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT FULLTEXTCATALOGPROPERTY(@catalog, 'PopulateStatus'), FULLTEXTCATALOGPROPERTY(@catalog, 'ItemCount')";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@catalog";
        parameter.Value = CatalogName;
        command.Parameters.Add(parameter);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);

        // NULL means the catalog does not exist: the full-text migration has not been applied.
        if (reader.IsDBNull(0) || reader.IsDBNull(1))
        {
            throw new InvalidOperationException($"Full-text catalog '{CatalogName}' does not exist.");
        }

        return (reader.GetInt32(0), reader.GetInt32(1));
    }
}
