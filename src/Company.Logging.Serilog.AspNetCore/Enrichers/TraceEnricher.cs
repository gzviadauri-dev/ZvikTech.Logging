using Serilog.Core;
using Serilog.Events;
using System.Diagnostics;

namespace Company.Logging.Serilog.AspNetCore.Enrichers;

/// <summary>
/// Enriches log events with OpenTelemetry / W3C trace context from the current <see cref="Activity"/>.
/// Fields follow ECS: <c>trace.id</c>, <c>span.id</c>, <c>transaction.id</c>.
/// </summary>
public sealed class TraceEnricher : ILogEventEnricher
{
    /// <inheritdoc />
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        var activity = Activity.Current;
        if (activity is null) return;

        // W3C format: 32 hex chars for trace, 16 hex chars for span
        var traceId = activity.TraceId.ToHexString();
        var spanId = activity.SpanId.ToHexString();

        if (!string.IsNullOrEmpty(traceId))
        {
            logEvent.AddPropertyIfAbsent(
                propertyFactory.CreateProperty("trace.id", traceId));
        }

        if (!string.IsNullOrEmpty(spanId))
        {
            logEvent.AddPropertyIfAbsent(
                propertyFactory.CreateProperty("span.id", spanId));

            // transaction.id mirrors span.id for the root span (Elastic APM convention)
            logEvent.AddPropertyIfAbsent(
                propertyFactory.CreateProperty("transaction.id", spanId));
        }
    }
}
