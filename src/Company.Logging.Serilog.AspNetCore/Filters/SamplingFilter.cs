using Company.Logging.Serilog.AspNetCore.Configuration;
using Serilog.Events;

namespace Company.Logging.Serilog.AspNetCore.Filters;

/// <summary>
/// Implements a deterministic sampling filter for successful HTTP request log events.
/// Errors and warnings are NEVER sampled out.
/// </summary>
public sealed class SamplingFilter
{
    private readonly SamplingOptions _options;
    private long _counter;

    /// <summary>Constructs with sampling options.</summary>
    public SamplingFilter(SamplingOptions options)
    {
        _options = options;
    }

    /// <summary>
    /// Returns <c>true</c> if the log event should be included.
    /// Called from <c>Filter.ByIncludingOnly</c>.
    /// </summary>
    public bool IsEnabled(LogEvent logEvent)
    {
        // Never sample out warnings, errors, or fatal events
        if (logEvent.Level >= LogEventLevel.Warning)
            return true;

        if (!_options.SampleSuccessRequests)
            return true;

        // Only apply sampling to HTTP request completion events
        if (!logEvent.Properties.TryGetValue("RequestPath", out _) &&
            !logEvent.Properties.TryGetValue("http.route", out _))
            return true;

        var rate = Math.Max(1, _options.SuccessSampleRate);
        var count = Interlocked.Increment(ref _counter);
        return count % rate == 0;
    }
}
