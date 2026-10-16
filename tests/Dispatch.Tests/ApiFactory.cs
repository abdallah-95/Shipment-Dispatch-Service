using Dispatch.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Serilog.Core;

namespace Dispatch.Tests;

/// <summary>
/// Hosts the real API pipeline in-process, pointed at the fixture's SQL Server. The API's own DI
/// wiring builds the DbContext (so VersionInterceptor comes from production registration) unless a
/// test adds extra interceptors.
/// </summary>
/// <remarks>
/// Set <see cref="LogSink"/> to capture the host's Serilog events. Production picks up any DI-registered
/// sink (ReadFrom.Services), so the events are the ones the console would get, with the same properties.
/// </remarks>
public sealed class ApiFactory(
    string connectionString,
    TimeProvider? timeProvider = null,
    params IInterceptor[] extraInterceptors) : WebApplicationFactory<Program>
{
    public InMemorySink? LogSink { get; init; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Default", connectionString);

        builder.ConfigureTestServices(services =>
        {
            if (LogSink is not null)
            {
                services.AddSingleton<ILogEventSink>(LogSink);
            }

            if (timeProvider is not null)
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton(timeProvider);
            }

            if (extraInterceptors.Length > 0)
            {
                // Same options as production (provider + VersionInterceptor), then the test's extras.
                var options = new DbContextOptionsBuilder<AppDbContext>();
                DependencyInjection.ConfigureDbContext(options, connectionString);
                options.AddInterceptors(extraInterceptors);

                services.RemoveAll<DbContextOptions<AppDbContext>>();
                services.AddSingleton(options.Options);
            }
        });
    }
}
