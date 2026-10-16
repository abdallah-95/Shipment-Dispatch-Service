using System.Net;
using System.Text.Json;
using Dispatch.Api.Middleware;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Events;

namespace Dispatch.Tests;

/// <summary>
/// Production has no route that throws, so this test host adds one: <see cref="ThrowingTestController"/>,
/// which lives in the test assembly and is registered only for this test's host.
/// </summary>
[Collection(TestDatabaseCollection.Name)]
public sealed class UnhandledExceptionTests(TestDatabaseFixture database)
{
    [Fact]
    public async Task Unhandled_exception_returns_generic_500_problem_with_correlation_id_and_is_logged_once()
    {
        var logs = new InMemorySink();
        await using var baseFactory = new ApiFactory(database.ConnectionString) { LogSink = logs };
        await using var factory = baseFactory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddControllers().AddApplicationPart(typeof(ThrowingTestController).Assembly)));
        using var client = factory.CreateClient();

        var response = await client.GetAsync(ThrowingTestController.Route);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var header = Assert.Single(response.Headers.GetValues(CorrelationIdMiddleware.HeaderName));
        var text = await response.Content.ReadAsStringAsync();
        using var body = JsonDocument.Parse(text);
        Assert.Equal(500, body.RootElement.GetProperty("status").GetInt32());
        Assert.Equal("An error occurred while processing your request.", body.RootElement.GetProperty("title").GetString());
        Assert.Equal(header, body.RootElement.GetProperty("correlationId").GetString());

        // Nothing about the exception reaches the client: not its message, type, or stack trace.
        Assert.DoesNotContain(ThrowingTestController.Message, text);
        Assert.DoesNotContain(nameof(InvalidOperationException), text);
        Assert.DoesNotContain(nameof(ThrowingTestController), text);
        Assert.False(body.RootElement.TryGetProperty("detail", out _));
        Assert.False(body.RootElement.TryGetProperty("exception", out _));

        // Logged exactly once with the exception, at Error, under the same correlation id.
        var logged = Assert.Single(logs.Events, e => e.Exception?.Message == ThrowingTestController.Message);
        Assert.Equal(LogEventLevel.Error, logged.Level);
        Assert.Equal(header, InMemorySink.Scalar(logged, CorrelationIdMiddleware.LogPropertyName));
    }
}

/// <summary>Test-only. Never registered by production code; see <see cref="UnhandledExceptionTests"/>.</summary>
[ApiController]
[Route(Route)]
public sealed class ThrowingTestController : ControllerBase
{
    public const string Route = "test/throw";
    public const string Message = "Test-only failure 5f0c2a7e: this text must never reach a response body.";

    [HttpGet]
    public ActionResult<string> Throw() => throw new InvalidOperationException(Message);
}
