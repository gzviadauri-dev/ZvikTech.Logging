using System.Diagnostics.Metrics;

namespace Company.Logging.Abstractions;

/// <summary>
/// Provides access to named <see cref="Meter"/> instances for recording application
/// metrics without hard-coding meter names in business code.
/// </summary>
/// <remarks>
/// The default meter name follows the convention <c>Company.{ServiceName}</c>.
/// Additional meters can be registered via <c>TelemetryOptions.Metrics.AdditionalMeters</c>.
/// </remarks>
public interface IMeterAccessor
{
    /// <summary>
    /// Returns the <see cref="Meter"/> for the given name.
    /// When <paramref name="name"/> is <see langword="null"/>, returns the default
    /// service meter (<c>Company.{ServiceName}</c>).
    /// </summary>
    /// <param name="name">
    /// Optional meter name. Must match a name registered with the OpenTelemetry SDK.
    /// </param>
    Meter GetMeter(string? name = null);
}
