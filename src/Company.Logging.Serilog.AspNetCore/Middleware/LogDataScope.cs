using Company.Logging.Abstractions;
using Company.Logging.Serilog.AspNetCore.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Company.Logging.Serilog.AspNetCore.Middleware;

/// <summary>
/// Default implementation of <see cref="ILogDataScope"/> that uses <see cref="ILogger{T}"/> scopes.
/// </summary>
internal sealed class LogDataScope : ILogDataScope
{
    private readonly ILogger<LogDataScope> _logger;
    private readonly HashSet<string> _metaKeyWhitelist;

    public LogDataScope(ILogger<LogDataScope> logger, IOptions<CompanyLoggingOptions> options)
    {
        _logger = logger;
        var whitelist = options.Value.MetaKeyWhitelist;
        _metaKeyWhitelist = whitelist.Count > 0
            ? new HashSet<string>(whitelist, StringComparer.OrdinalIgnoreCase)
            : [];
    }

    /// <inheritdoc />
    public IDisposable? BeginScope(LogData data)
    {
        var state = new Dictionary<string, object?>();

        foreach (var (k, v) in data.Tags)
            state[$"tag.{k}"] = v;

        foreach (var (k, v) in data.Meta)
        {
            // Enforce whitelist: when non-empty, only allow listed keys
            if (_metaKeyWhitelist.Count == 0 || _metaKeyWhitelist.Contains(k))
                state[$"meta.{k}"] = v;
        }

        if (data.EventName is not null) state["event.action"] = data.EventName;
        if (data.EntityType is not null) state["entity.type"] = data.EntityType;
        if (data.EntityId is not null) state["entity.id"] = data.EntityId;

        return _logger.BeginScope(state);
    }
}
