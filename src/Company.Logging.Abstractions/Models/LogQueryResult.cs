namespace Company.Logging.Abstractions;

/// <summary>
/// A paged result set returned by <see cref="ILogReader.QueryAsync"/>.
/// </summary>
public sealed class LogQueryResult
{
    /// <summary>The log entries on the current page.</summary>
    public IReadOnlyList<LogEntry> Entries { get; init; } = Array.Empty<LogEntry>();

    /// <summary>Total number of matching entries across all pages.</summary>
    public long TotalCount { get; init; }

    /// <summary>Zero-based page index used for this result.</summary>
    public int PageIndex { get; init; }

    /// <summary>Page size used for this result.</summary>
    public int PageSize { get; init; }

    /// <summary>Whether more pages are available after this one.</summary>
    public bool HasMore => (long)(PageIndex + 1) * PageSize < TotalCount;
}
