using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Dispatch.Api.Swagger;

/// <summary>
/// MVC advertises every registered output formatter (text/plain, text/json, ...), but the API only
/// ever answers application/json, and application/problem+json for errors. This corrects the
/// document; it does not change what is sent.
/// </summary>
internal sealed class ResponseContentTypesFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (operation.Responses is null)
        {
            return;
        }

        foreach (var (code, response) in operation.Responses)
        {
            if (response is not OpenApiResponse concrete || concrete.Content is null || concrete.Content.Count == 0)
            {
                continue;
            }

            var isError = int.TryParse(code, out var status) && status >= 400;
            var mediaType = concrete.Content.Values.First();
            concrete.Content = new Dictionary<string, OpenApiMediaType>
            {
                [isError ? "application/problem+json" : "application/json"] = mediaType,
            };
        }
    }
}
