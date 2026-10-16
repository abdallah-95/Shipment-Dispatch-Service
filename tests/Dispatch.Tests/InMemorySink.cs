using System.Collections.Concurrent;
using Dispatch.Api.Middleware;
using Serilog.Core;
using Serilog.Events;

namespace Dispatch.Tests;

/// <summary>Collects every Serilog event a test host writes. Thread-safe: requests log concurrently.</summary>
public sealed class InMemorySink : ILogEventSink
{
    private readonly ConcurrentQueue<LogEvent> _events = new();

    public IReadOnlyCollection<LogEvent> Events => _events;

    public void Emit(LogEvent logEvent) => _events.Enqueue(logEvent);

    /// <summary>The events written while handling the request with this correlation id.</summary>
    public IReadOnlyList<LogEvent> ForCorrelationId(string correlationId) =>
        _events.Where(e => Scalar(e, CorrelationIdMiddleware.LogPropertyName) as string == correlationId).ToList();

    /// <summary>A scalar property's raw value, or null if the event does not have it.</summary>
    public static object? Scalar(LogEvent logEvent, string property) =>
        logEvent.Properties.TryGetValue(property, out var value) && value is ScalarValue scalar ? scalar.Value : null;
}
