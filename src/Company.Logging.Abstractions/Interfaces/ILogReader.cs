namespace Company.Logging.Abstractions;

/// <summary>
/// Reads structured log entries back from the Elasticsearch store.
/// Inject this wherever you need log search or audit trail functionality.
/// </summary>
public interface ILogReader
{
    /// <summary>
    /// Queries log entries using the provided filters and pagination.
    /// Returns the most recent entries first.
    /// </summary>
    /// <param name="query">Filter and pagination parameters. All fields are optional.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<LogQueryResult> QueryAsync(LogQuery query, CancellationToken ct = default);

    /// <summary>
    /// Verifies the full write → read pipeline against Elasticsearch.
    /// Writes a probe log document (with a unique correlation ID) directly to ES,
    /// immediately reads it back, deletes it, and returns the retrieved entry.
    /// Use this for health checks and connectivity validation.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the written document cannot be found after indexing.
    /// </exception>
    Task<LogEntry> ProbeAsync(CancellationToken ct = default);
}
