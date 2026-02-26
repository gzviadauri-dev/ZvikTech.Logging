using Microsoft.Extensions.Logging;

namespace Company.Logging.Abstractions;

/// <summary>
/// Parameters for querying structured log entries from Elasticsearch.
/// All filters are combined with AND logic. Null fields are ignored.
/// </summary>
public sealed class LogQuery
{
    /// <summary>Filter by service name (exact match). E.g. "orders-api".</summary>
    public string? ServiceName { get; init; }

    /// <summary>Filter by deployment environment. E.g. "prod", "dev".</summary>
    public string? Environment { get; init; }

    /// <summary>Include only entries at this level or above.</summary>
    public LogLevel? MinLevel { get; init; }

    /// <summary>Start of the time window (inclusive). Defaults to last 24 hours when both are null.</summary>
    public DateTimeOffset? From { get; init; }

    /// <summary>End of the time window (inclusive).</summary>
    public DateTimeOffset? To { get; init; }

    /// <summary>Filter by an exact correlation ID.</summary>
    public string? CorrelationId { get; init; }

    /// <summary>Filter by an exact W3C trace ID (32 hex chars).</summary>
    public string? TraceId { get; init; }

    /// <summary>Full-text search against the log message field.</summary>
    public string? SearchText { get; init; }

    /// <summary>Number of results per page. Default: 50. Max: 1000.</summary>
    public int PageSize { get; init; } = 50;

    /// <summary>Zero-based page index.</summary>
    public int PageIndex { get; init; } = 0;
}
