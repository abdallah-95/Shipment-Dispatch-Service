using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;

namespace Dispatch.Tests;

/// <summary>
/// Building the OpenAPI document throws on enum and ProblemDetails mistakes, so fetching it is
/// the check that the Swagger setup works at all.
/// </summary>
[Collection(TestDatabaseCollection.Name)]
public sealed class SwaggerTests(TestDatabaseFixture database)
{
    [Fact]
    public async Task Enabled_swagger_document_contains_every_route_and_enums_as_strings()
    {
        await using var baseFactory = new ApiFactory(database.ConnectionString);
        await using var factory = baseFactory.WithWebHostBuilder(builder => builder.UseSetting("Swagger:Enabled", "true"));
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/swagger/v1/swagger.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var paths = document.RootElement.GetProperty("paths");
        Assert.True(paths.GetProperty("/api/shipments").TryGetProperty("get", out _));
        Assert.True(paths.GetProperty("/api/shipments/{id}").TryGetProperty("get", out _));
        Assert.True(paths.GetProperty("/api/shipments/{id}/status").TryGetProperty("post", out _));

        var status = document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty("ShipmentStatus");
        Assert.Equal("string", status.GetProperty("type").GetString());
        Assert.Contains("PickedUp", status.GetProperty("enum").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public async Task Swagger_is_off_by_default()
    {
        await using var factory = new ApiFactory(database.ConnectionString);
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/swagger/v1/swagger.json")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/swagger")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/")).StatusCode);
    }
}
