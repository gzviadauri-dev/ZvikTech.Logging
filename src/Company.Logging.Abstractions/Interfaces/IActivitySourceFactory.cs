using System.Diagnostics;

namespace Company.Logging.Abstractions;

/// <summary>
/// Factory that supplies named <see cref="ActivitySource"/> instances for creating
/// distributed trace spans without hard-coding source names in application code.
/// </summary>
/// <remarks>
/// The default source name follows the convention <c>Company.{ServiceName}</c>.
/// Register custom sources by calling <c>AddCompanyTelemetry()</c> and listing them
/// in <c>TelemetryOptions.Tracing.AdditionalSources</c>.
/// </remarks>
public interface IActivitySourceFactory
{
    /// <summary>
    /// Returns the <see cref="ActivitySource"/> for the given name.
    /// When <paramref name="name"/> is <see langword="null"/>, returns the default
    /// service source (<c>Company.{ServiceName}</c>).
    /// </summary>
    /// <param name="name">
    /// Optional source name. Must match a name that was registered with the
    /// OpenTelemetry SDK, otherwise spans will be silently dropped.
    /// </param>
    ActivitySource GetSource(string? name = null);
}
