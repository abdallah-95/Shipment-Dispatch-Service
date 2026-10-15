namespace Dispatch.Api.Swagger;

/// <summary>
/// Swagger is opt-in through the <c>Swagger:Enabled</c> setting (default false). It is deliberately
/// not tied to the hosting environment: switching a container to Development would change other behaviour.
/// There is no auth scheme in the document.
/// </summary>
internal static class SwaggerSetup
{
    private const string EnabledKey = "Swagger:Enabled";

    public static bool IsEnabled(this IConfiguration configuration) => configuration.GetValue<bool>(EnabledKey);

    public static IServiceCollection AddSwaggerDocs(this IServiceCollection services)
    {
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new() { Title = "Shipment Dispatch Service", Version = "v1" });

            // Action summaries come from the XML doc comments on the controllers.
            options.IncludeXmlComments(Path.Combine(AppContext.BaseDirectory, $"{typeof(SwaggerSetup).Assembly.GetName().Name}.xml"));

            options.SchemaFilter<ChangeStatusRequestExampleFilter>();
            options.OperationFilter<StatusProblemResponsesFilter>();
            options.OperationFilter<ResponseContentTypesFilter>();
        });

        return services;
    }

    public static WebApplication UseSwaggerDocs(this WebApplication app)
    {
        app.UseSwagger();
        app.UseSwaggerUI(options => options.SwaggerEndpoint("/swagger/v1/swagger.json", "v1"));

        // Only exists while Swagger is on; with the flag off, "/" stays a plain 404.
        app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();

        return app;
    }
}
