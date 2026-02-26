namespace Company.Logging.Abstractions;

/// <summary>
/// A single structured log entry returned from Elasticsearch.
/// Field names follow Elastic Common Schema (ECS).
/// </summary>
public sealed class LogEntry
{
    /// <summary>When the event occurred (ECS: @timestamp).</summary>
    public DateTimeOffset Timestamp { get; init; }

    /// <summary>Log level string as stored in ECS: verbose, debug, information, warning, error, fatal.</summary>
    public string Level { get; init; } = string.Empty;

    /// <summary>The rendered log message.</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>ECS: service.name</summary>
    public string? ServiceName { get; init; }

    /// <summary>ECS: service.version</summary>
    public string? ServiceVersion { get; init; }

    /// <summary>ECS: service.instance.id — hostname or pod name.</summary>
    public string? InstanceId { get; init; }

    /// <summary>ECS: deployment.environment</summary>
    public string? Environment { get; init; }

    /// <summary>ECS: correlation.id — propagated X-Correlation-Id header.</summary>
    public string? CorrelationId { get; init; }

    /// <summary>ECS: trace.id — W3C 32-char hex trace ID.</summary>
    public string? TraceId { get; init; }

    /// <summary>ECS: span.id — W3C 16-char hex span ID.</summary>
    public string? SpanId { get; init; }

    /// <summary>ECS: user.id — authenticated user identifier.</summary>
    public string? UserId { get; init; }

    /// <summary>ECS: event.action — domain event name set via LogData.WithEvent().</summary>
    public string? EventAction { get; init; }

    /// <summary>Entity type set via LogData.WithEntity().</summary>
    public string? EntityType { get; init; }

    /// <summary>Entity ID set via LogData.WithEntity().</summary>
    public string? EntityId { get; init; }

    /// <summary>All remaining ECS fields not mapped above, keyed by dotted field path.</summary>
    public IReadOnlyDictionary<string, string?> Extra { get; init; }
        = new Dictionary<string, string?>();
}
