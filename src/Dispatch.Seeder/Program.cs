using System.Diagnostics;
using Dispatch.Infrastructure;
using Dispatch.Seeder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

var builder = Host.CreateApplicationBuilder(args);

var connectionString = builder.Configuration.GetConnectionString(DependencyInjection.ConnectionStringName);
if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine($"Connection string '{DependencyInjection.ConnectionStringName}' is not configured.");
    return 1;
}

builder.Services.AddInfrastructure(connectionString);

using var host = builder.Build();
var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Dispatch.Seeder");

try
{
    await using var scope = host.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    logger.LogInformation("Applying migrations");
    await db.Database.MigrateAsync(cts.Token);
    logger.LogInformation("Migrations applied");

    await db.Database.OpenConnectionAsync(cts.Token);
    var connection = (SqlConnection)db.Database.GetDbConnection();

    if (await db.Shipments.AnyAsync(cts.Token))
    {
        logger.LogInformation("already seeded, skipping");
    }
    else
    {
        var stopwatch = Stopwatch.StartNew();
        var dataset = DatasetGenerator.Generate();
        logger.LogInformation(
            "Generated {CustomerCount} customers, {DriverCount} drivers, {ShipmentCount} shipments, {HistoryCount} history rows in {ElapsedMs} ms",
            dataset.Customers.Count, dataset.Drivers.Count, dataset.Shipments.Count, dataset.History.Count, stopwatch.ElapsedMilliseconds);

        stopwatch.Restart();
        await BulkInserter.InsertAsync(connection, dataset, cts.Token);
        logger.LogInformation("Bulk insert committed in {ElapsedMs} ms", stopwatch.ElapsedMilliseconds);

        foreach (var group in dataset.Shipments.GroupBy(s => s.Status).OrderBy(g => g.Key))
        {
            logger.LogInformation("Seeded {Status} shipments: {Count}", group.Key, group.Count());
        }
    }

    // On both paths, so the API never starts against an incomplete full-text index: not after a fresh
    // seed, not on a volume that gained the index after it was seeded, not on a retry after a failed wait.
    var shipmentCount = await db.Shipments.CountAsync(cts.Token);
    if (!await SearchReadiness.WaitForPopulationAsync(connection, shipmentCount, logger, cts.Token)
        || !await SearchReadiness.SmokeSearchAsync(db, logger, cts.Token))
    {
        return 1;
    }

    return 0;
}
catch (Exception ex)
{
    logger.LogCritical(ex, "Seeder failed");
    return 1;
}
