using Dispatch.Infrastructure;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString(DependencyInjection.ConnectionStringName)
    ?? throw new InvalidOperationException(
        $"Connection string '{DependencyInjection.ConnectionStringName}' is not configured.");

builder.Services.AddInfrastructure(connectionString);

// "live" has no checks: it only proves the process is up. "ready" is every check tagged "ready".
builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>("sqlserver", tags: ["ready"]);

var app = builder.Build();

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

app.Run();
