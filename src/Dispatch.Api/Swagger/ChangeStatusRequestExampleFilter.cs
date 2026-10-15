using System.Text.Json.Nodes;
using Dispatch.Application.Shipments;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Dispatch.Api.Swagger;

/// <summary>Example body for <c>POST /api/shipments/{id}/status</c>.</summary>
internal sealed class ChangeStatusRequestExampleFilter : ISchemaFilter
{
    public void Apply(IOpenApiSchema schema, SchemaFilterContext context)
    {
        if (schema is OpenApiSchema concrete && context.Type == typeof(ChangeShipmentStatusRequest))
        {
            concrete.Example = new JsonObject
            {
                ["status"] = "PickedUp",
                ["expectedVersion"] = 0,
                ["driverId"] = 1,
            };
        }
    }
}
