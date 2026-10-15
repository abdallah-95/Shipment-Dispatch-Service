using Dispatch.Application;
using Dispatch.Infrastructure.Interceptors;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Dispatch.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "Default";

    // Stateless, so one instance serves every context.
    private static readonly VersionInterceptor VersionInterceptor = new();

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<AppDbContext>(options => ConfigureDbContext(options, connectionString));
        services.AddScoped<IDispatchDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        return services;
    }

    /// <summary>
    /// The one place AppDbContext options are built: the provider plus the save-time interceptors.
    /// DI and the test fixture both use it, so no context can be created without the interceptors.
    /// </summary>
    public static void ConfigureDbContext(DbContextOptionsBuilder options, string connectionString) =>
        options
            .UseSqlServer(connectionString)
            .AddInterceptors(VersionInterceptor);
}
