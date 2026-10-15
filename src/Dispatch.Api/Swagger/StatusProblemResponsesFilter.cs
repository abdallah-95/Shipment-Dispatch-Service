using Dispatch.Api.Controllers;
using Dispatch.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Dispatch.Api.Swagger;

/// <summary>
/// The 409 and 422 bodies of the status action are ProblemDetails with extra members
/// (<c>currentStatus</c>, <c>currentVersion</c>, <c>allowedTransitions</c>). The controller adds them
/// as ProblemDetails extensions at runtime, so a plain ProblemDetails schema would hide them. This
/// filter documents them without changing the runtime response.
/// </summary>
internal sealed class StatusProblemResponsesFilter : IOperationFilter
{
    private static readonly System.Reflection.MethodInfo ChangeStatusAction =
        typeof(ShipmentsController).GetMethod(nameof(ShipmentsController.ChangeStatus))!;

    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (context.MethodInfo != ChangeStatusAction || operation.Responses is null)
        {
            return;
        }

        var problem = context.SchemaGenerator.GenerateSchema(typeof(ProblemDetails), context.SchemaRepository);
        var status = context.SchemaGenerator.GenerateSchema(typeof(ShipmentStatus), context.SchemaRepository);

        IOpenApiSchema StatusList() => new OpenApiSchema { Type = JsonSchemaType.Array, Items = status };

        var conflict = context.SchemaRepository.AddDefinition(
            "VersionConflictProblemDetails",
            new OpenApiSchema
            {
                AllOf = [problem, new OpenApiSchema
                {
                    Type = JsonSchemaType.Object,
                    Required = new HashSet<string> { "currentStatus", "currentVersion", "allowedTransitions" },
                    Properties = new Dictionary<string, IOpenApiSchema>
                    {
                        ["currentStatus"] = status,
                        ["currentVersion"] = new OpenApiSchema { Type = JsonSchemaType.Integer, Format = "int32" },
                        ["allowedTransitions"] = StatusList(),
                    },
                }],
            });

        var illegal = context.SchemaRepository.AddDefinition(
            "IllegalTransitionProblemDetails",
            new OpenApiSchema
            {
                AllOf = [problem, new OpenApiSchema
                {
                    Type = JsonSchemaType.Object,
                    Properties = new Dictionary<string, IOpenApiSchema>
                    {
                        ["currentStatus"] = status,
                        ["allowedTransitions"] = StatusList(),
                    },
                }],
            });

        Describe(operation, "409",
            "The shipment is not at `expectedVersion` (stale, or a concurrent request won). " +
            "The body carries the shipment's current state: `currentStatus`, `currentVersion` and " +
            "`allowedTransitions` (the statuses reachable from `currentStatus`). Re-read and retry.",
            conflict);

        Describe(operation, "422",
            "The request is well-formed but not allowed. Either the transition is illegal, or " +
            "moving to PickedUp without the id of an existing driver. " +
            "For an illegal transition the body also carries `currentStatus` and `allowedTransitions`; " +
            "for a missing or unknown driver it does not.",
            illegal);
    }

    private static void Describe(OpenApiOperation operation, string code, string description, IOpenApiSchema schema)
    {
        if (operation.Responses!.TryGetValue(code, out var response) && response is OpenApiResponse concrete)
        {
            concrete.Description = description;
            foreach (var media in concrete.Content!.Values)
            {
                media.Schema = schema;
            }
        }
    }
}
