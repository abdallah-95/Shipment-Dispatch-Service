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

    if (await db.Shipments.AnyAsync(cts.Token))
    {
        logger.LogInformation("already seeded, skipping");
        return 0;
    }

    var stopwatch = Stopwatch.StartNew();
    var dataset = DatasetGenerator.Generate();
    logger.LogInformation(
        "Generated {CustomerCount} customers, {DriverCount} drivers, {ShipmentCount} shipments, {HistoryCount} history rows in {ElapsedMs} ms",
        dataset.Customers.Count, dataset.Drivers.Count, dataset.Shipments.Count, dataset.History.Count, stopwatch.ElapsedMilliseconds);

    stopwatch.Restart();
    await db.Database.OpenConnectionAsync(cts.Token);
    await BulkInserter.InsertAsync((SqlConnection)db.Database.GetDbConnection(), dataset, cts.Token);
    logger.LogInformation("Bulk insert committed in {ElapsedMs} ms", stopwatch.ElapsedMilliseconds);

    foreach (var group in dataset.Shipments.GroupBy(s => s.Status).OrderBy(g => g.Key))
    {
        logger.LogInformation("Seeded {Status} shipments: {Count}", group.Key, group.Count());
    }

    return 0;
}
catch (Exception ex)
{
    logger.LogCritical(ex, "Seeder failed");
    return 1;
}
