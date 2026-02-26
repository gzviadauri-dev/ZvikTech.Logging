using System.Diagnostics;
using System.Diagnostics.Metrics;
using Company.Logging.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Company.Logging.Telemetry.AspNetCore.Configuration;

namespace Company.Logging.Telemetry.AspNetCore.Metrics;

/// <summary>
/// Owns the application-level metric instruments:
/// <list type="bullet">
///   <item><c>company.http.requests.total</c> — counter of completed HTTP requests.</item>
///   <item><c>company.http.requests.duration</c> — histogram of request duration in ms.</item>
///   <item><c>company.http.requests.active</c> — up-down counter of in-flight requests.</item>
///   <item><c>company.errors.total</c> — counter of unhandled exceptions, tagged by type.</item>
/// </list>
/// This class is registered as a singleton and exposed through DI so the
/// <see cref="CompanyMetricsMiddleware"/> can record observations.
/// </summary>
public sealed class CompanyMetricsCollector : IDisposable
{
    // ── Instruments ───────────────────────────────────────────────────────────

    /// <summary>Total HTTP requests completed.</summary>
    public readonly Counter<long>         HttpRequestsTotal;
    /// <summary>HTTP request duration histogram (ms).</summary>
    public readonly Histogram<double>     HttpRequestsDuration;
    /// <summary>In-flight HTTP request count.</summary>
    public readonly UpDownCounter<long>   HttpRequestsActive;
    /// <summary>Unhandled exceptions, tagged by exception type.</summary>
    public readonly Counter<long>         ErrorsTotal;

    private readonly Meter _meter;
    private bool _disposed;

    /// <summary>Creates and registers all metric instruments.</summary>
    public CompanyMetricsCollector(IMeterAccessor meterAccessor)
    {
        _meter = meterAccessor.GetMeter();

        HttpRequestsTotal = _meter.CreateCounter<long>(
            "company.http.requests.total",
            unit: "{request}",
            description: "Total number of completed HTTP requests.");

        HttpRequestsDuration = _meter.CreateHistogram<double>(
            "company.http.requests.duration",
            unit: "ms",
            description: "Duration of HTTP requests in milliseconds.");

        HttpRequestsActive = _meter.CreateUpDownCounter<long>(
            "company.http.requests.active",
            unit: "{request}",
            description: "Number of HTTP requests currently being processed.");

        ErrorsTotal = _meter.CreateCounter<long>(
            "company.errors.total",
            unit: "{error}",
            description: "Total number of unhandled exceptions, tagged by exception.type.");
    }

    /// <summary>
    /// Records an HTTP request observation. Called by <see cref="CompanyMetricsMiddleware"/>
    /// after the response is sent.
    /// </summary>
    public void RecordRequest(string method, string route, int statusCode, double durationMs)
    {
        var tags = new TagList
        {
            { "http.method",      method },
            { "http.route",       route },
            { "http.status_code", statusCode }
        };

        HttpRequestsTotal.Add(1, tags);
        HttpRequestsDuration.Record(durationMs, tags);
    }

    /// <summary>Records an unhandled exception, tagging by its CLR type name.</summary>
    public void RecordError(Exception ex, string method, string route)
    {
        ErrorsTotal.Add(1, new TagList
        {
            { "exception.type", ex.GetType().Name },
            { "http.method",    method },
            { "http.route",     route }
        });
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        // The Meter is owned by DefaultMeterAccessor; do not dispose here.
    }
}

/// <summary>
/// ASP.NET Core middleware that records <see cref="CompanyMetricsCollector"/> observations
/// for every HTTP request using a <see cref="Stopwatch"/> for latency measurement.
/// </summary>
public sealed class CompanyMetricsMiddleware : IMiddleware
{
    private readonly CompanyMetricsCollector _collector;

    /// <summary>Injects the singleton metrics collector.</summary>
    public CompanyMetricsMiddleware(CompanyMetricsCollector collector)
    {
        _collector = collector;
    }

    /// <inheritdoc />
    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        var method = context.Request.Method;
        var sw = Stopwatch.StartNew();

        _collector.HttpRequestsActive.Add(1);
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            var route = context.GetEndpoint()?.DisplayName ?? context.Request.Path.Value ?? "unknown";
            _collector.RecordError(ex, method, route);
            throw;
        }
        finally
        {
            sw.Stop();
            _collector.HttpRequestsActive.Add(-1);

            // Route is only available after the endpoint has been matched
            var routePattern = context.GetEndpoint()?.DisplayName
                               ?? context.Request.Path.Value
                               ?? "unknown";

            _collector.RecordRequest(method, routePattern, context.Response.StatusCode, sw.Elapsed.TotalMilliseconds);
        }
    }
}
