using Dispatch.Api.Json;
using Dispatch.Api.Middleware;
using Dispatch.Api.Swagger;
using Dispatch.Application;
using Dispatch.Infrastructure;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;

var builder = WebApplication.CreateBuilder(args);

// Compact JSON to the console. Levels come from the "Serilog" configuration section; ReadFrom.Services
// adds any ILogEventSink registered in DI (the tests register an in-memory one).
builder.Services.AddSerilog((services, logger) => logger
    .ReadFrom.Configuration(services.GetRequiredService<IConfiguration>())
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .WriteTo.Console(new CompactJsonFormatter()));

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
// The customisation runs for all of them, including the ones controllers build with Problem(), so
// every error body carries the correlationId that is also on the response header.
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
{
    if (CorrelationIdMiddleware.Get(context.HttpContext) is { } correlationId)
    {
        context.ProblemDetails.Extensions["correlationId"] = correlationId;
    }
});

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

// Order matters. The correlation id comes first, so every later log line (the request line included)
// carries it. The exception handler sits inside request logging, so an exception is logged once, by
// the handler, and request logging sees only the 500 it produced.
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseSerilogRequestLogging(options =>
{
    // Probes call the health endpoints every few seconds. Their request lines are Debug, below the
    // minimum level; a failing check is still logged, by the health check service.
    var defaultLevel = options.GetLevel;
    options.GetLevel = (context, elapsedMs, exception) =>
        context.Request.Path.StartsWithSegments("/health")
            ? LogEventLevel.Debug
            : defaultLevel(context, elapsedMs, exception);
});
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
