using Dispatch.Application.Dashboard;
using Dispatch.Application.Shipments;
using Microsoft.Extensions.DependencyInjection;

namespace Dispatch.Application;

public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IShipmentQueryService, ShipmentQueryService>();
        services.AddScoped<IShipmentListService, ShipmentListService>();
        services.AddScoped<IShipmentStatusService, ShipmentStatusService>();
        services.AddScoped<IDashboardService, DashboardService>();
        return services;
    }
}
