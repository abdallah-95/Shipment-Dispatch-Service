using Dispatch.Application.Shipments;
using Microsoft.Extensions.DependencyInjection;

namespace Dispatch.Application;

public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IShipmentQueryService, ShipmentQueryService>();
        services.AddScoped<IShipmentStatusService, ShipmentStatusService>();
        return services;
    }
}
