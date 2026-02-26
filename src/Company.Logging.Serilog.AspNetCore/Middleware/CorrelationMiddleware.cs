using System.Diagnostics;
using Company.Logging.Abstractions;
using Company.Logging.Serilog.AspNetCore.Configuration;
using Company.Logging.Serilog.AspNetCore.Enrichers;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Serilog.Context;

namespace Company.Logging.Serilog.AspNetCore.Middleware;

/// <summary>
/// ASP.NET Core middleware that:
/// <list type="bullet">
///   <item>Reads the <c>X-Correlation-Id</c> header (or configured header name) from the incoming request.</item>
///   <item>Generates a new <see cref="Guid"/> correlation ID if none is present.</item>
///   <item>Stores the correlation ID in <see cref="CorrelationContext"/> (AsyncLocal) for enrichers.</item>
///   <item>Pushes <c>correlation.id</c> and <c>request.id</c> into the Serilog <see cref="LogContext"/>.</item>
///   <item>Adds the correlation ID as a response header.</item>
///   <item>Registers the service as <see cref="ICorrelationContext"/> for downstream DI consumers.</item>
/// </list>
/// </summary>
public sealed class CorrelationMiddleware : IMiddleware
{
    private readonly IOptions<CompanyLoggingOptions> _options;

    // Fallback ActivitySource used when IActivitySourceFactory is not registered
    // (i.e. Company.Logging.Telemetry.AspNetCore is not installed).
    private static readonly ActivitySource FallbackSource =
        new("Company.Logging.CorrelationMiddleware", "1.0.0");

    /// <summary>Injects options via DI.</summary>
    public CorrelationMiddleware(IOptions<CompanyLoggingOptions> options)
    {
        _options = options;
    }

    // Max safe length for a correlation ID stored in every log event.
    // A GUID "D" format is 36 chars; allow generous headroom for custom schemes.
    private const int MaxCorrelationIdLength = 128;

    /// <inheritdoc />
    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        var headerName = _options.Value.CorrelationHeader;

        // Read or generate correlation ID; reject overlong values to prevent log injection
        var raw = context.Request.Headers.TryGetValue(headerName, out var values)
            ? values.FirstOrDefault()
            : null;

        var correlationId = !string.IsNullOrWhiteSpace(raw) && raw.Length <= MaxCorrelationIdLength
            ? raw
            : Guid.NewGuid().ToString("D");

        var requestId = context.TraceIdentifier;

        // Store in AsyncLocal so enricher + ICorrelationContext can access it
        CorrelationContext.CorrelationId = correlationId;
        CorrelationContext.RequestId = requestId;

        // Add to response so clients can correlate
        context.Response.OnStarting(() =>
        {
            context.Response.Headers.TryAdd(headerName, correlationId);
            return Task.CompletedTask;
        });

        // ── Span: start an Activity for this request only when no parent exists ──
        // This provides a baseline trace even without full OTEL instrumentation.
        // When AddAspNetCoreInstrumentation() is active, Activity.Current will already
        // be set before this middleware runs, so we skip to avoid double-spanning.
        var activityName = $"HTTP {context.Request.Method} {context.Request.Path}";
        Activity? activity = null;
        if (Activity.Current is null)
        {
            activity = FallbackSource.StartActivity(activityName, ActivityKind.Server);
            activity?.SetTag("http.method", context.Request.Method);
            activity?.SetTag("http.route",  context.Request.Path.Value);
        }

        try
        {
            // Push into Serilog LogContext for this execution context
            using (LogContext.PushProperty("correlation.id", correlationId))
            using (LogContext.PushProperty("request.id", requestId))
            {
                // Register a scoped ICorrelationContext so application code can inject it
                context.Items[typeof(ICorrelationContext)] = new HttpCorrelationContext(correlationId, requestId);

                await next(context);
            }
        }
        finally
        {
            if (activity is not null)
            {
                activity.SetTag("http.status_code", context.Response.StatusCode);
                activity.Dispose();
            }
        }
    }
}

/// <summary>
/// Scoped implementation of <see cref="ICorrelationContext"/> populated per request.
/// </summary>
internal sealed class HttpCorrelationContext : ICorrelationContext
{
    public HttpCorrelationContext(string correlationId, string? requestId)
    {
        CorrelationId = correlationId;
        RequestId = requestId;
    }

    /// <inheritdoc />
    public string CorrelationId { get; }

    /// <inheritdoc />
    public string? RequestId { get; }
}
