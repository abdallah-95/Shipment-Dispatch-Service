using Dispatch.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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

    // Phase 0: schema only. Data seeding is added in a later phase.
    return 0;
}
catch (Exception ex)
{
    logger.LogCritical(ex, "Seeder failed");
    return 1;
}
