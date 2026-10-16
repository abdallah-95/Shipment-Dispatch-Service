using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dispatch.Api.Middleware;
using Dispatch.Domain;
using Serilog.Events;

namespace Dispatch.Tests;

/// <summary>The X-Correlation-Id header, the correlationId of error bodies, and the CorrelationId log property.</summary>
[Collection(TestDatabaseCollection.Name)]
public sealed class CorrelationIdTests(TestDatabaseFixture database) : IAsyncLifetime
{
    private readonly InMemorySink _logs = new();
    private ApiFactory _factory = null!;
    private HttpClient _client = null!;

    public Task InitializeAsync()
    {
        _factory = new ApiFactory(database.ConnectionString) { LogSink = _logs };
        _client = _factory.CreateClient();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task Incoming_traceparent_trace_id_is_the_correlation_id()
    {
        var traceId = ActivityTraceId.CreateRandom().ToHexString();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.Add("traceparent", TraceParent(traceId));

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(traceId, CorrelationHeader(response));
    }

    [Fact]
    public async Task Without_traceparent_each_request_gets_a_new_32_character_hex_id()
    {
        var first = CorrelationHeader(await _client.GetAsync("/health/live"));
        var second = CorrelationHeader(await _client.GetAsync("/health/live"));

        Assert.Matches("^[0-9a-f]{32}$", first);
        Assert.Matches("^[0-9a-f]{32}$", second);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public async Task Stale_version_409_carries_the_correlation_id_in_header_body_and_warning_log()
    {
        var shipment = await TestData.CreateShipmentAsync(database, ShipmentStatus.InTransit);
        var staleVersion = shipment.Version - 1;
        var traceId = ActivityTraceId.CreateRandom().ToHexString();

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/shipments/{shipment.Id}/status")
        {
            Content = JsonContent.Create(new { status = "OutForDelivery", expectedVersion = staleVersion }),
        };
        request.Headers.Add("traceparent", TraceParent(traceId));

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var header = CorrelationHeader(response);
        Assert.Equal(traceId, header);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(header, body.RootElement.GetProperty("correlationId").GetString());

        var warning = Assert.Single(_logs.ForCorrelationId(header), e => e.Level == LogEventLevel.Warning);
        Assert.Equal(shipment.Id, InMemorySink.Scalar(warning, "ShipmentId"));
        Assert.Equal(staleVersion, InMemorySink.Scalar(warning, "ExpectedVersion"));
        Assert.Equal(shipment.Version, InMemorySink.Scalar(warning, "CurrentVersion"));
    }

    private static string TraceParent(string traceId) => $"00-{traceId}-{ActivitySpanId.CreateRandom().ToHexString()}-01";

    private static string CorrelationHeader(HttpResponseMessage response) =>
        Assert.Single(response.Headers.GetValues(CorrelationIdMiddleware.HeaderName));
}
