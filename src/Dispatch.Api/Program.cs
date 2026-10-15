using Dispatch.Api.Json;
using Dispatch.Api.Swagger;
using Dispatch.Application;
using Dispatch.Infrastructure;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString(DependencyInjection.ConnectionStringName)
    ?? throw new InvalidOperationException(
        $"Connection string '{DependencyInjection.ConnectionStringName}' is not configured.");

builder.Services.AddInfrastructure(connectionString);
builder.Services.AddApplication();
builder.Services.AddSingleton(TimeProvider.System);

// Enums travel as names; numbers and numeric strings are rejected (see EnumNameConverterFactory).
builder.Services
    .AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new EnumNameConverterFactory()));

// Every error response is ProblemDetails: validation failures ([ApiController]), unhandled
// exceptions (UseExceptionHandler) and bare status codes such as route misses (UseStatusCodePages).
builder.Services.AddProblemDetails();

// Off unless Swagger:Enabled is true (compose sets it for the local stack).
var swaggerEnabled = builder.Configuration.IsEnabled();
if (swaggerEnabled)
{
    builder.Services.AddSwaggerDocs();
}

// "live" has no checks: it only proves the process is up. "ready" is every check tagged "ready".
builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>("sqlserver", tags: ["ready"]);

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (swaggerEnabled)
{
    app.UseSwaggerDocs();
}

app.MapControllers();
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

app.Run();

// Exposes the entry point to WebApplicationFactory in the test project.
public partial class Program;
