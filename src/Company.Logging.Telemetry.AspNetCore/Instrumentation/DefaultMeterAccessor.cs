using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Company.Logging.Abstractions;
using Company.Logging.Telemetry.AspNetCore.Configuration;
using Microsoft.Extensions.Options;

namespace Company.Logging.Telemetry.AspNetCore.Instrumentation;

/// <summary>
/// Singleton <see cref="IMeterAccessor"/> that caches <see cref="Meter"/> instances by name.
/// The default meter name is <c>"Company.{ServiceName}"</c>.
/// </summary>
public sealed class DefaultMeterAccessor : IMeterAccessor, IDisposable
{
    private readonly string _defaultMeterName;
    private readonly string _version;
    private readonly ConcurrentDictionary<string, Meter> _meters = new(StringComparer.Ordinal);
    private bool _disposed;

    /// <summary>Initializes the accessor using resolved telemetry options.</summary>
    public DefaultMeterAccessor(IOptions<TelemetryOptions> options)
    {
        var opts = options.Value;
        var svcName    = string.IsNullOrWhiteSpace(opts.ServiceName)    ? "unknown-service" : opts.ServiceName;
        _defaultMeterName = $"Company.{svcName}";
        _version          = string.IsNullOrWhiteSpace(opts.ServiceVersion) ? "1.0.0" : opts.ServiceVersion;
    }

    /// <inheritdoc />
    public Meter GetMeter(string? name = null)
    {
        var key = string.IsNullOrWhiteSpace(name) ? _defaultMeterName : name;
        return _meters.GetOrAdd(key, k => new Meter(k, _version));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var meter in _meters.Values)
            meter.Dispose();
        _meters.Clear();
    }
}
