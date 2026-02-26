using Company.Logging.Serilog.AspNetCore.Configuration;
using Microsoft.Extensions.Options;
using Serilog.Core;
using Serilog.Events;

namespace Company.Logging.Serilog.AspNetCore.Enrichers;

/// <summary>
/// Enriches every log event with deployment environment fields:
/// <c>deployment.environment</c> and optionally <c>cloud.region</c>.
/// </summary>
public sealed class EnvironmentEnricher : ILogEventEnricher
{
    private readonly LogEventProperty _envProperty;
    private readonly LogEventProperty? _regionProperty;

    /// <summary>Initializes with resolved options.</summary>
    public EnvironmentEnricher(IOptions<CompanyLoggingOptions> options)
    {
        var o = options.Value;
        _envProperty = new LogEventProperty("deployment.environment", new ScalarValue(o.Environment));
        if (!string.IsNullOrWhiteSpace(o.Region))
            _regionProperty = new LogEventProperty("cloud.region", new ScalarValue(o.Region));
    }

    /// <inheritdoc />
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        logEvent.AddPropertyIfAbsent(_envProperty);
        if (_regionProperty is not null)
            logEvent.AddPropertyIfAbsent(_regionProperty);
    }
}
