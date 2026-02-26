using Company.Logging.Serilog.AspNetCore.Configuration;
using Microsoft.Extensions.Options;
using Serilog.Core;
using Serilog.Events;
using System.Reflection;

namespace Company.Logging.Serilog.AspNetCore.Enrichers;

/// <summary>
/// Enriches every log event with ECS-compatible service identity fields:
/// <c>service.name</c>, <c>service.version</c>, <c>service.instance.id</c>.
/// </summary>
public sealed class ServiceEnricher : ILogEventEnricher
{
    private readonly string _serviceName;
    private readonly string _serviceVersion;
    private readonly string _instanceId;

    // Cache scalar values to avoid allocation per log event
    private readonly LogEventProperty _nameProperty;
    private readonly LogEventProperty _versionProperty;
    private readonly LogEventProperty _instanceProperty;

    /// <summary>Initializes with resolved options.</summary>
    public ServiceEnricher(IOptions<CompanyLoggingOptions> options)
    {
        var o = options.Value;
        _serviceName = o.ServiceName;
        _serviceVersion = o.ServiceVersion
            ?? Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3)
            ?? "0.0.0";
        _instanceId = System.Environment.MachineName;

        _nameProperty = new LogEventProperty("service.name", new ScalarValue(_serviceName));
        _versionProperty = new LogEventProperty("service.version", new ScalarValue(_serviceVersion));
        _instanceProperty = new LogEventProperty("service.instance.id", new ScalarValue(_instanceId));
    }

    /// <inheritdoc />
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        logEvent.AddPropertyIfAbsent(_nameProperty);
        logEvent.AddPropertyIfAbsent(_versionProperty);
        logEvent.AddPropertyIfAbsent(_instanceProperty);
    }
}
