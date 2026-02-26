using Microsoft.Extensions.Logging;

namespace Company.Logging.Abstractions;

/// <summary>
/// Allows application code to push structured log data into the current logging scope.
/// </summary>
public interface ILogDataScope
{
    /// <summary>
    /// Begins a logging scope that includes the provided <see cref="LogData"/> as structured properties.
    /// Dispose the returned scope to remove the properties from the context.
    /// </summary>
    IDisposable? BeginScope(LogData data);
}
