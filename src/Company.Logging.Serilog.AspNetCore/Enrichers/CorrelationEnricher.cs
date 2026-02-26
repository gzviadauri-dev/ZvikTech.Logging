using Serilog.Core;
using Serilog.Events;

namespace Company.Logging.Serilog.AspNetCore.Enrichers;

/// <summary>
/// Enriches log events with <c>correlation.id</c> and <c>request.id</c>
/// from the ambient <see cref="CorrelationContext"/>.
/// </summary>
public sealed class CorrelationEnricher : ILogEventEnricher
{
    /// <inheritdoc />
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        var correlationId = CorrelationContext.CorrelationId;
        if (!string.IsNullOrEmpty(correlationId))
        {
            logEvent.AddPropertyIfAbsent(
                propertyFactory.CreateProperty("correlation.id", correlationId));
        }

        var requestId = CorrelationContext.RequestId;
        if (!string.IsNullOrEmpty(requestId))
        {
            logEvent.AddPropertyIfAbsent(
                propertyFactory.CreateProperty("request.id", requestId));
        }
    }
}

/// <summary>
/// AsyncLocal storage for correlation context, set by <see cref="Middleware.CorrelationMiddleware"/>.
/// </summary>
public static class CorrelationContext
{
    private static readonly AsyncLocal<string?> _correlationId = new();
    private static readonly AsyncLocal<string?> _requestId = new();

    /// <summary>Current correlation ID for the ambient execution context.</summary>
    public static string? CorrelationId
    {
        get => _correlationId.Value;
        internal set => _correlationId.Value = value;
    }

    /// <summary>Current request ID for the ambient execution context.</summary>
    public static string? RequestId
    {
        get => _requestId.Value;
        internal set => _requestId.Value = value;
    }
}
