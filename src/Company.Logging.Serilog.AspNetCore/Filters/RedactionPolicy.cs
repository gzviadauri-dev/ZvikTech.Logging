using Company.Logging.Serilog.AspNetCore.Configuration;
using Serilog.Core;
using Serilog.Events;

namespace Company.Logging.Serilog.AspNetCore.Filters;

/// <summary>
/// Serilog <see cref="ILogEventEnricher"/> that redacts sensitive property values.
/// Any property whose name (case-insensitive) matches a configured sensitive key
/// is replaced with the configured redacted value (default: "***").
/// </summary>
public sealed class RedactionPolicy : ILogEventEnricher
{
    private readonly HashSet<string> _sensitiveKeys;
    private readonly string _redactedValue;

    /// <summary>Constructs the policy from options.</summary>
    public RedactionPolicy(RedactionOptions options)
    {
        _sensitiveKeys = new HashSet<string>(
            options.SensitiveKeys,
            StringComparer.OrdinalIgnoreCase);
        _redactedValue = options.RedactedValue;
    }

    /// <inheritdoc />
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        foreach (var key in logEvent.Properties.Keys.ToList())
        {
            if (_sensitiveKeys.Contains(key))
            {
                logEvent.AddOrUpdateProperty(
                    propertyFactory.CreateProperty(key, _redactedValue));
            }
        }
    }
}
