using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dispatch.Application.Shipments;
using Dispatch.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace Dispatch.Tests;

/// <summary>POST /api/shipments/{id}/status through the real HTTP pipeline.</summary>
[Collection(TestDatabaseCollection.Name)]
public sealed class ShipmentStatusTests(TestDatabaseFixture database) : IAsyncLifetime
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly FakeTimeProvider _clock = TestData.CreateClock();
    private ApiFactory _factory = null!;
    private HttpClient _client = null!;

    public Task InitializeAsync()
    {
        _factory = new ApiFactory(database.ConnectionString, _clock);
        _client = _factory.CreateClient();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    public static TheoryData<ShipmentStatus, ShipmentStatus> LegalTransitions()
    {
        var data = new TheoryData<ShipmentStatus, ShipmentStatus>();
        foreach (var (from, targets) in ShipmentTransitions.Map)
        {
            foreach (var to in targets)
            {
                data.Add(from, to);
            }
        }

        return data;
    }

    /// <summary>The full status cross product minus the legal edges. Same-status pairs included.</summary>
    public static TheoryData<ShipmentStatus, ShipmentStatus> IllegalTransitions()
    {
        var data = new TheoryData<ShipmentStatus, ShipmentStatus>();
        foreach (var from in Enum.GetValues<ShipmentStatus>())
        {
            foreach (var to in Enum.GetValues<ShipmentStatus>())
            {
                if (!ShipmentTransitions.IsAllowed(from, to))
                {
                    data.Add(from, to);
                }
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(LegalTransitions))]
    public async Task Legal_transition_returns_200_increments_version_and_writes_one_history_row(
        ShipmentStatus from, ShipmentStatus to)
    {
        var shipment = await TestData.CreateShipmentAsync(database, from);
        int? driverId = to == ShipmentStatus.PickedUp ? (await TestData.CreateDriverAsync(database)).Id : null;
        var historyBefore = await TestData.LoadHistoryAsync(database, shipment.Id);
        var now = _clock.GetUtcNow().UtcDateTime;

        var response = await PostStatusAsync(_client, shipment.Id, to, shipment.Version, driverId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ShipmentStatusChangedDto>(Json);
        Assert.NotNull(body);
        Assert.Equal(shipment.Id, body.Id);
        Assert.Equal(to, body.Status);
        Assert.Equal(shipment.Version + 1, body.Version);
        Assert.Equal(now, body.UpdatedAt);
        Assert.Equal(driverId ?? shipment.DriverId, body.DriverId);

        var stored = await TestData.LoadShipmentAsync(database, shipment.Id);
        Assert.Equal(to, stored.Status);
        Assert.Equal(shipment.Version + 1, stored.Version);
        Assert.Equal(now, stored.UpdatedAt);
        Assert.Equal(body.DriverId, stored.DriverId);

        var historyAfter = await TestData.LoadHistoryAsync(database, shipment.Id);
        var added = Assert.Single(historyAfter.Skip(historyBefore.Count));
        Assert.Equal(from, added.FromStatus);
        Assert.Equal(to, added.ToStatus);
        Assert.Equal(stored.UpdatedAt, added.ChangedAt);
    }

    [Theory]
    [MemberData(nameof(IllegalTransitions))]
    public async Task Illegal_transition_returns_422_with_current_state_and_changes_nothing(
        ShipmentStatus from, ShipmentStatus to)
    {
        var shipment = await TestData.CreateShipmentAsync(database, from);
        // A valid driver for PickedUp targets, so the driver rule cannot be what rejects the request.
        int? driverId = to == ShipmentStatus.PickedUp ? (await TestData.CreateDriverAsync(database)).Id : null;
        var historyBefore = await TestData.LoadHistoryAsync(database, shipment.Id);

        var response = await PostStatusAsync(_client, shipment.Id, to, shipment.Version, driverId);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.Equal(422, problem.GetProperty("status").GetInt32());
        Assert.Equal(from.ToString(), problem.GetProperty("currentStatus").GetString());
        Assert.Equal(Names(ShipmentTransitions.AllowedFrom(from)), StringArray(problem, "allowedTransitions"));

        await AssertUnchangedAsync(shipment, historyBefore.Count);
    }

    [Fact]
    public async Task Stale_expectedVersion_returns_409_with_current_state_even_when_transition_is_illegal()
    {
        var shipment = await TestData.CreateShipmentAsync(database, ShipmentStatus.InTransit);
        var historyBefore = await TestData.LoadHistoryAsync(database, shipment.Id);

        // Delivered is illegal from InTransit; the stale version must win and produce a 409, not a 422.
        var response = await PostStatusAsync(
            _client, shipment.Id, ShipmentStatus.Delivered, shipment.Version - 1, driverId: null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.Equal(409, problem.GetProperty("status").GetInt32());
        Assert.Equal(nameof(ShipmentStatus.InTransit), problem.GetProperty("currentStatus").GetString());
        Assert.Equal(shipment.Version, problem.GetProperty("currentVersion").GetInt32());
        Assert.Equal([nameof(ShipmentStatus.OutForDelivery)], StringArray(problem, "allowedTransitions"));

        await AssertUnchangedAsync(shipment, historyBefore.Count);
    }

    [Fact]
    public async Task Parallel_requests_with_same_version_give_one_200_one_409_and_one_history_row()
    {
        var shipment = await TestData.CreateShipmentAsync(database, ShipmentStatus.Created);
        var driver = await TestData.CreateDriverAsync(database);
        var historyBefore = await TestData.LoadHistoryAsync(database, shipment.Id);

        // Both requests load the shipment before either may save, so the loser is caught by the
        // save-time concurrency check (DbUpdateConcurrencyException), not the early version comparison.
        var barrier = new SaveBarrierInterceptor(participants: 2);
        await using var factory = new ApiFactory(database.ConnectionString, _clock, barrier);
        using var client = factory.CreateClient();

        var responses = await Task.WhenAll(
            PostStatusAsync(client, shipment.Id, ShipmentStatus.PickedUp, shipment.Version, driver.Id),
            PostStatusAsync(client, shipment.Id, ShipmentStatus.PickedUp, shipment.Version, driver.Id));

        Assert.True(barrier.AllArrived, "Both requests should have reached SaveChanges.");
        Assert.Equal(
            [HttpStatusCode.OK, HttpStatusCode.Conflict],
            responses.Select(r => r.StatusCode).OrderBy(code => code));

        // The winner already moved it to the requested status; the loser still gets a 409.
        var problem = await ReadProblemAsync(responses.Single(r => r.StatusCode == HttpStatusCode.Conflict));
        Assert.Equal(nameof(ShipmentStatus.PickedUp), problem.GetProperty("currentStatus").GetString());
        Assert.Equal(shipment.Version + 1, problem.GetProperty("currentVersion").GetInt32());
        Assert.Equal([nameof(ShipmentStatus.InTransit)], StringArray(problem, "allowedTransitions"));

        var stored = await TestData.LoadShipmentAsync(database, shipment.Id);
        Assert.Equal(shipment.Version + 1, stored.Version);
        var historyAfter = await TestData.LoadHistoryAsync(database, shipment.Id);
        Assert.Single(historyAfter.Skip(historyBefore.Count));
    }

    [Fact]
    public async Task Missing_expectedVersion_returns_400_and_does_not_bind_as_zero()
    {
        // A Created shipment is at version 0, so if a missing value became 0 this request would succeed.
        var shipment = await TestData.CreateShipmentAsync(database, ShipmentStatus.Created);
        var driver = await TestData.CreateDriverAsync(database);
        Assert.Equal(0, shipment.Version);

        var response = await PostRawAsync(shipment.Id, $$"""{"status":"PickedUp","driverId":{{driver.Id}}}""");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.True(problem.GetProperty("errors").TryGetProperty("ExpectedVersion", out _));
        await AssertUnchangedAsync(shipment, historyCount: 1);
    }

    [Theory]
    [InlineData("\"Bogus\"")]
    [InlineData("1")] // PickedUp's integer value: integers are not accepted for enums
    [InlineData("\"1\"")]
    [InlineData("99")]
    [InlineData("null")]
    public async Task Invalid_status_returns_400(string statusJson)
    {
        var shipment = await TestData.CreateShipmentAsync(database, ShipmentStatus.Created);
        var driver = await TestData.CreateDriverAsync(database);

        var response = await PostRawAsync(
            shipment.Id, $$"""{"status":{{statusJson}},"expectedVersion":0,"driverId":{{driver.Id}}}""");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await ReadProblemAsync(response);
        await AssertUnchangedAsync(shipment, historyCount: 1);
    }

    [Fact]
    public async Task Unknown_shipment_returns_404()
    {
        var response = await PostStatusAsync(_client, 2_000_000_000, ShipmentStatus.InTransit, 0, driverId: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.Equal(404, problem.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task PickedUp_without_driverId_returns_422()
    {
        var shipment = await TestData.CreateShipmentAsync(database, ShipmentStatus.Created);

        var response = await PostStatusAsync(_client, shipment.Id, ShipmentStatus.PickedUp, shipment.Version, driverId: null);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        await ReadProblemAsync(response);
        await AssertUnchangedAsync(shipment, historyCount: 1);
    }

    [Fact]
    public async Task PickedUp_with_unknown_driverId_returns_422()
    {
        var shipment = await TestData.CreateShipmentAsync(database, ShipmentStatus.Created);

        var response = await PostStatusAsync(_client, shipment.Id, ShipmentStatus.PickedUp, shipment.Version, int.MaxValue);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        await ReadProblemAsync(response);
        await AssertUnchangedAsync(shipment, historyCount: 1);
    }

    [Fact]
    public async Task DriverId_on_a_transition_other_than_PickedUp_returns_400()
    {
        var shipment = await TestData.CreateShipmentAsync(database, ShipmentStatus.PickedUp);
        var otherDriver = await TestData.CreateDriverAsync(database);
        var historyBefore = await TestData.LoadHistoryAsync(database, shipment.Id);

        var response = await PostStatusAsync(_client, shipment.Id, ShipmentStatus.InTransit, shipment.Version, otherDriver.Id);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await ReadProblemAsync(response);
        await AssertUnchangedAsync(shipment, historyBefore.Count);
    }

    [Fact]
    public async Task SearchText_only_change_keeps_Version_and_a_client_at_that_version_can_still_advance()
    {
        var shipment = await TestData.CreateShipmentAsync(database, ShipmentStatus.Created);
        var driver = await TestData.CreateDriverAsync(database);
        var newSearchText = SearchTextNormalizer.Build(shipment.TrackingNumber, "Renamed Customer", shipment.Destination);

        await using (var db = database.CreateDbContext())
        {
            var tracked = await db.Shipments.SingleAsync(s => s.Id == shipment.Id);
            tracked.SearchText = newSearchText;
            Assert.Equal(1, await db.SaveChangesAsync());
        }

        var afterRecompute = await TestData.LoadShipmentAsync(database, shipment.Id);
        Assert.Equal(newSearchText, afterRecompute.SearchText);
        Assert.Equal(shipment.Version, afterRecompute.Version);

        var response = await PostStatusAsync(_client, shipment.Id, ShipmentStatus.PickedUp, shipment.Version, driver.Id);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ShipmentStatusChangedDto>(Json);
        Assert.Equal(shipment.Version + 1, body?.Version);
    }

    private static Task<HttpResponseMessage> PostStatusAsync(
        HttpClient client, int shipmentId, ShipmentStatus status, int expectedVersion, int? driverId) =>
        client.PostAsJsonAsync(
            $"/api/shipments/{shipmentId}/status",
            new { status, expectedVersion, driverId },
            Json);

    private Task<HttpResponseMessage> PostRawAsync(int shipmentId, string json) =>
        _client.PostAsync(
            $"/api/shipments/{shipmentId}/status",
            new StringContent(json, Encoding.UTF8, "application/json"));

    private static async Task<JsonElement> ReadProblemAsync(HttpResponseMessage response)
    {
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    private static string[] StringArray(JsonElement problem, string property) =>
        problem.GetProperty(property).EnumerateArray().Select(e => e.GetString()!).ToArray();

    private static string[] Names(IEnumerable<ShipmentStatus> statuses) =>
        statuses.Select(s => s.ToString()).ToArray();

    /// <summary>The shipment row and its history are exactly as inserted.</summary>
    private async Task AssertUnchangedAsync(Dispatch.Domain.Entities.Shipment original, int historyCount)
    {
        var stored = await TestData.LoadShipmentAsync(database, original.Id);
        Assert.Equal(original.Status, stored.Status);
        Assert.Equal(original.Version, stored.Version);
        Assert.Equal(original.DriverId, stored.DriverId);
        Assert.Equal(original.UpdatedAt, stored.UpdatedAt);
        Assert.Equal(historyCount, (await TestData.LoadHistoryAsync(database, original.Id)).Count);
    }
}
