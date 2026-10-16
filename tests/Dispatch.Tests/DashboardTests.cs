using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dispatch.Application.Dashboard;
using Dispatch.Domain;

namespace Dispatch.Tests;

/// <summary>
/// GET /api/dashboard/summary. The test database is shared with every other test class, so these
/// assert shape and deltas, never exact totals.
/// </summary>
/// <remarks>
/// The delta test must not overlap another class's inserts. It does not need a collection of its own:
/// every class that writes to the database is in <see cref="TestDatabaseCollection"/>, and xUnit runs
/// the classes of one collection one at a time. A separate collection would also get its own fixture,
/// which means a second database.
/// </remarks>
[Collection(TestDatabaseCollection.Name)]
public sealed class DashboardTests(TestDatabaseFixture database) : IAsyncLifetime
{
    /// <summary>Spelled out, not read from the enum, so that reordering the enum fails the test.</summary>
    private static readonly ShipmentStatus[] WorkflowOrder =
    [
        ShipmentStatus.Created,
        ShipmentStatus.PickedUp,
        ShipmentStatus.InTransit,
        ShipmentStatus.OutForDelivery,
        ShipmentStatus.Delivered,
        ShipmentStatus.Failed,
        ShipmentStatus.Returned,
    ];

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
    public async Task Summary_lists_all_seven_statuses_in_workflow_order_and_total_is_their_sum()
    {
        // At least one shipment, so the sum is not trivially 0 = 0.
        await TestData.CreateShipmentAsync(database, ShipmentStatus.Delivered);

        var summary = await GetSummaryAsync();

        Assert.Equal(WorkflowOrder, summary.ByStatus.Select(s => s.Status));
        Assert.All(summary.ByStatus, s => Assert.True(s.Count >= 0, $"{s.Status} has a negative count"));
        Assert.Equal(summary.ByStatus.Sum(s => s.Count), summary.Total);
        Assert.True(summary.Total > 0);
    }

    [Fact]
    public async Task Inserting_shipments_in_one_status_raises_that_status_and_the_total_by_exactly_that_number()
    {
        const int inserted = 3;
        var before = await GetSummaryAsync();

        var createdAt = new DateTime(2026, 3, 1, 9, 0, 0, DateTimeKind.Utc);
        await TestData.CreateListShipmentsAsync(
            database,
            Enumerable.Range(0, inserted)
                .Select(_ => new ListRow(ShipmentStatus.Created, createdAt, $"Dashboard Customer {Guid.NewGuid():N}", null)));

        var after = await GetSummaryAsync();

        foreach (var status in WorkflowOrder)
        {
            var expected = Count(before, status) + (status == ShipmentStatus.Created ? inserted : 0);
            Assert.Equal(expected, Count(after, status));
        }

        Assert.Equal(before.Total + inserted, after.Total);
    }

    private async Task<DashboardSummaryDto> GetSummaryAsync()
    {
        var response = await _client.GetAsync("/api/dashboard/summary");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<DashboardSummaryDto>(Json))!;
    }

    private static int Count(DashboardSummaryDto summary, ShipmentStatus status) =>
        summary.ByStatus.Single(s => s.Status == status).Count;
}
