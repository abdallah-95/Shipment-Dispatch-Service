using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dispatch.Application.Shipments;
using Dispatch.Domain;
using Dispatch.Domain.Entities;
using Microsoft.AspNetCore.Mvc;

namespace Dispatch.Tests;

[Collection(TestDatabaseCollection.Name)]
public sealed class ShipmentDetailTests(TestDatabaseFixture database) : IAsyncLifetime
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private ApiFactory _factory = null!;
    private HttpClient _client = null!;

    public Task InitializeAsync()
    {
        _factory = new ApiFactory(database.ConnectionString);
        _client = _factory.CreateClient();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task Detail_returns_200_with_history_ordered_by_ChangedAt_then_Id()
    {
        var unique = Guid.NewGuid().ToString("N");
        var trackingNumber = $"TST{unique}"[..32]; // column is varchar(32)
        var t0 = new DateTime(2026, 1, 5, 9, 0, 0, DateTimeKind.Utc);
        var t1 = t0.AddHours(3);

        await using var db = database.CreateDbContext();
        var customer = new Customer { Name = $"Detail Test Customer {unique}" };
        var driver = new Driver { Name = $"Detail Test Driver {unique}" };
        var shipment = new Shipment
        {
            TrackingNumber = trackingNumber,
            Customer = customer,
            Driver = driver,
            Origin = "Central Hub",
            Destination = "Port Said",
            WeightKg = 12.5m,
            Status = ShipmentStatus.InTransit,
            CreatedAt = t0,
            UpdatedAt = t1,
            Version = 2,
            SearchText = SearchTextNormalizer.Build(trackingNumber, customer.Name, "Port Said"),
        };

        // Inserted deliberately out of order: Ids ascend in insertion order, so ordering by Id alone
        // would give PickedUp, Created, InTransit. The correct order is by ChangedAt, with Id breaking
        // the tie between the two rows that share t1.
        shipment.History.Add(new ShipmentStatusHistory { FromStatus = ShipmentStatus.Created, ToStatus = ShipmentStatus.PickedUp, ChangedAt = t1 });
        shipment.History.Add(new ShipmentStatusHistory { FromStatus = null, ToStatus = ShipmentStatus.Created, ChangedAt = t0 });
        shipment.History.Add(new ShipmentStatusHistory { FromStatus = ShipmentStatus.PickedUp, ToStatus = ShipmentStatus.InTransit, ChangedAt = t1 });
        db.Shipments.Add(shipment);
        await db.SaveChangesAsync();

        var response = await _client.GetAsync($"/api/shipments/{shipment.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var detail = await response.Content.ReadFromJsonAsync<ShipmentDetailDto>(Json);
        Assert.NotNull(detail);
        Assert.Equal(shipment.Id, detail.Id);
        Assert.Equal(trackingNumber, detail.TrackingNumber);
        Assert.Equal(ShipmentStatus.InTransit, detail.Status);
        Assert.Equal(2, detail.Version);
        Assert.Equal(customer.Name, detail.Customer.Name);
        Assert.Equal(driver.Name, detail.Driver?.Name);
        Assert.Equal(DateTimeKind.Utc, detail.CreatedAt.Kind);

        Assert.Equal(
            [ShipmentStatus.Created, ShipmentStatus.PickedUp, ShipmentStatus.InTransit],
            detail.History.Select(h => h.ToStatus));
        Assert.Null(detail.History[0].FromStatus);
    }

    [Fact]
    public async Task Detail_for_unknown_id_returns_404_problem_details()
    {
        var response = await _client.GetAsync("/api/shipments/2000000000");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(Json);
        Assert.NotNull(problem);
        Assert.Equal(404, problem.Status);
    }
}
