using System.Diagnostics;
using Serilog.Context;

namespace Dispatch.Api.Middleware;

/// <summary>
/// Gives each request one correlation id: the W3C trace id of the request's Activity (ASP.NET Core
/// continues an incoming <c>traceparent</c>, otherwise starts a new trace), or a new Guid when there is
/// no Activity. The id is a <c>CorrelationId</c> log property for the whole request, the
/// <c>X-Correlation-Id</c> response header, and the <c>correlationId</c> of every ProblemDetails body.
/// Registered before request logging, so the request log line carries it too.
/// </summary>
public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Correlation-Id";
    public const string LogPropertyName = "CorrelationId";

    private static readonly object ItemKey = new();

    /// <summary>The current request's correlation id, or null outside this middleware.</summary>
    public static string? Get(HttpContext context) =>
        context.Items.TryGetValue(ItemKey, out var id) ? id as string : null;

    public async Task InvokeAsync(HttpContext context)
    {
        var activity = Activity.Current;
        var correlationId = activity is { IdFormat: ActivityIdFormat.W3C }
            ? activity.TraceId.ToHexString()
            : Guid.NewGuid().ToString("N");

        context.Items[ItemKey] = correlationId;

        // On starting, not now: the exception handler clears the response headers before it writes a 500.
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        using (LogContext.PushProperty(LogPropertyName, correlationId))
        {
            await next(context);
        }
    }
}
