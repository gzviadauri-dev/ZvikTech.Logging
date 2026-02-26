using System.Collections.Concurrent;
using System.Diagnostics;
using Company.Logging.Abstractions;
using Company.Logging.Telemetry.AspNetCore.Configuration;
using Microsoft.Extensions.Options;

namespace Company.Logging.Telemetry.AspNetCore.Instrumentation;

/// <summary>
/// Singleton <see cref="IActivitySourceFactory"/> that caches <see cref="ActivitySource"/>
/// instances by name. The default source name is <c>"Company.{ServiceName}"</c>.
/// </summary>
public sealed class DefaultActivitySourceFactory : IActivitySourceFactory, IDisposable
{
    private readonly string _defaultSourceName;
    private readonly ConcurrentDictionary<string, ActivitySource> _sources = new(StringComparer.Ordinal);
    private bool _disposed;

    /// <summary>Initializes the factory using resolved telemetry options.</summary>
    public DefaultActivitySourceFactory(IOptions<TelemetryOptions> options)
    {
        var opts = options.Value;
        var svcName = string.IsNullOrWhiteSpace(opts.ServiceName) ? "unknown-service" : opts.ServiceName;
        _defaultSourceName = $"Company.{svcName}";
    }

    /// <inheritdoc />
    public ActivitySource GetSource(string? name = null)
    {
        var key = string.IsNullOrWhiteSpace(name) ? _defaultSourceName : name;
        return _sources.GetOrAdd(key, static k => new ActivitySource(k));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var source in _sources.Values)
            source.Dispose();
        _sources.Clear();
    }
}
