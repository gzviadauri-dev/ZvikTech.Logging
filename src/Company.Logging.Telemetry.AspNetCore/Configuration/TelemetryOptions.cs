using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace Company.Logging.Telemetry.AspNetCore.Configuration;

/// <summary>
/// Root telemetry configuration section bound from
/// <c>"CompanyLogging:Telemetry"</c> in <c>appsettings.json</c>.
/// </summary>
public sealed class TelemetryOptions
{
    /// <summary>Configuration key relative to the <c>CompanyLogging</c> section.</summary>
    public const string SectionName = "CompanyLogging:Telemetry";

    /// <summary>Master switch. Set to <c>false</c> to disable all telemetry with one toggle.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Override the service name used in trace resource attributes.
    /// Falls back to <c>CompanyLoggingOptions.ServiceName</c> when empty.
    /// </summary>
    public string ServiceName { get; set; } = string.Empty;

    /// <summary>
    /// Override the service version used in trace resource attributes.
    /// Falls back to <c>CompanyLoggingOptions.ServiceVersion</c> when empty.
    /// </summary>
    public string ServiceVersion { get; set; } = string.Empty;

    /// <summary>OTLP (OpenTelemetry Protocol) exporter settings.</summary>
    [ValidateObjectMembers]
    public OtlpOptions Otlp { get; set; } = new();

    /// <summary>Elastic APM agent settings.</summary>
    [ValidateObjectMembers]
    public ElasticApmOptions ElasticApm { get; set; } = new();

    /// <summary>Distributed tracing settings.</summary>
    [ValidateObjectMembers]
    public TracingOptions Tracing { get; set; } = new();

    /// <summary>Application metrics settings.</summary>
    [ValidateObjectMembers]
    public MetricsOptions Metrics { get; set; } = new();
}

/// <summary>OTLP exporter configuration.</summary>
public sealed class OtlpOptions
{
    /// <summary>Enable the OTLP exporter.</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>OTLP collector endpoint. E.g. "http://localhost:4317" for gRPC.</summary>
    [Required(AllowEmptyStrings = false)]
    public string Endpoint { get; set; } = "http://localhost:4317";

    /// <summary>Transport protocol: <c>grpc</c> or <c>http/protobuf</c>.</summary>
    public string Protocol { get; set; } = "grpc";
}

/// <summary>Elastic APM agent configuration.</summary>
public sealed class ElasticApmOptions
{
    /// <summary>Enable the Elastic APM auto-instrumentation agent.</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>APM Server URL. E.g. "http://localhost:8200".</summary>
    public string ServerUrl { get; set; } = "http://localhost:8200";

    /// <summary>Elastic Cloud API key for APM Server authentication.</summary>
    public string? ApiKey { get; set; }

    /// <summary>APM Server secret token (alternative to API key).</summary>
    public string? SecretToken { get; set; }
}

/// <summary>Distributed tracing configuration.</summary>
public sealed class TracingOptions
{
    /// <summary>Enable tracing pipeline. Requires <see cref="TelemetryOptions.Enabled"/> = true.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Fraction of traces to sample: <c>1.0</c> = 100%, <c>0.1</c> = 10%.
    /// Values outside [0.0, 1.0] are clamped. Use <c>1.0</c> for dev/staging.
    /// </summary>
    [Range(0.0, 1.0)]
    public double SampleRatio { get; set; } = 1.0;

    /// <summary>Auto-instrument ASP.NET Core incoming requests.</summary>
    public bool InstrumentAspNetCore { get; set; } = true;

    /// <summary>Auto-instrument outgoing <see cref="System.Net.Http.HttpClient"/> calls.</summary>
    public bool InstrumentHttpClient { get; set; } = true;

    /// <summary>Auto-instrument Entity Framework Core queries (requires separate EF package).</summary>
    public bool InstrumentEntityFramework { get; set; } = false;

    /// <summary>Auto-instrument SqlClient queries (requires separate SqlClient package).</summary>
    public bool InstrumentSqlClient { get; set; } = false;

    /// <summary>
    /// Additional custom <c>ActivitySource</c> names to include in the tracing pipeline.
    /// The default source <c>"Company.{ServiceName}"</c> is always included.
    /// </summary>
    public List<string> AdditionalSources { get; set; } = new();
}

/// <summary>Application metrics configuration.</summary>
public sealed class MetricsOptions
{
    /// <summary>Enable metrics pipeline. Requires <see cref="TelemetryOptions.Enabled"/> = true.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Instrument ASP.NET Core request metrics.</summary>
    public bool InstrumentAspNetCore { get; set; } = true;

    /// <summary>Instrument outgoing <see cref="System.Net.Http.HttpClient"/> metrics.</summary>
    public bool InstrumentHttpClient { get; set; } = true;

    /// <summary>Instrument .NET runtime metrics (GC, threadpool, heap).</summary>
    public bool InstrumentRuntime { get; set; } = true;

    /// <summary>
    /// Additional custom <c>Meter</c> names to include in the metrics pipeline.
    /// The default meter <c>"Company.{ServiceName}"</c> is always included.
    /// </summary>
    public List<string> AdditionalMeters { get; set; } = new();
}
