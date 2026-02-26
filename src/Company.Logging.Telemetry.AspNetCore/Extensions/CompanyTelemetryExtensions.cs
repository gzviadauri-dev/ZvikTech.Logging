using Company.Logging.Abstractions;
using Company.Logging.Telemetry.AspNetCore.Configuration;
using Company.Logging.Telemetry.AspNetCore.Instrumentation;
using Company.Logging.Telemetry.AspNetCore.Metrics;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Company.Logging.Telemetry.AspNetCore.Extensions;

/// <summary>
/// Extension methods that wire up the full OpenTelemetry observability stack
/// (traces + metrics) alongside the existing Serilog logging pipeline.
/// </summary>
public static class CompanyTelemetryExtensions
{
    /// <summary>
    /// Registers OpenTelemetry tracing and metrics, application-level metric instruments,
    /// <see cref="IActivitySourceFactory"/> and <see cref="IMeterAccessor"/> in the DI container.
    /// </summary>
    /// <example>
    /// <code>
    /// // Program.cs
    /// builder.Services.AddCompanyLogging(builder.Configuration);
    /// builder.Services.AddCompanyTelemetry(builder.Configuration);
    ///
    /// // Then in middleware pipeline:
    /// app.UseCompanyTelemetry();
    ///
    /// // Inject into endpoints / handlers:
    /// app.MapPost("/orders", (IActivitySourceFactory asf, ...) =>
    /// {
    ///     using var span = asf.GetSource().StartActivity("ProcessOrder");
    ///     span?.SetTag("order.id", orderId);
    ///     ...
    /// });
    /// </code>
    /// </example>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Application configuration (reads <c>CompanyLogging:Telemetry</c>).</param>
    /// <param name="configureOptions">Optional in-code override of <see cref="TelemetryOptions"/>.</param>
    public static IServiceCollection AddCompanyTelemetry(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<TelemetryOptions>? configureOptions = null)
    {
        // ── Options registration ─────────────────────────────────────────────
        services
            .AddOptions<TelemetryOptions>()
            .Bind(configuration.GetSection(TelemetryOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        if (configureOptions is not null)
            services.Configure(configureOptions);

        // Resolve a local copy directly from IConfiguration — avoids BuildServiceProvider()
        // which would create a second DI root and log spurious "scoped service" warnings.
        var opts = new TelemetryOptions();
        configuration.GetSection(TelemetryOptions.SectionName).Bind(opts);
        configureOptions?.Invoke(opts);

        if (!opts.Enabled)
            return services;

        // Service name / version for resource attributes — resolved from options
        var serviceName    = string.IsNullOrWhiteSpace(opts.ServiceName)    ? "unknown-service" : opts.ServiceName;
        var serviceVersion = string.IsNullOrWhiteSpace(opts.ServiceVersion) ? "1.0.0"           : opts.ServiceVersion;
        var defaultSource  = $"Company.{serviceName}";
        var defaultMeter   = $"Company.{serviceName}";

        // ── Abstractions ─────────────────────────────────────────────────────
        services.AddSingleton<IActivitySourceFactory, DefaultActivitySourceFactory>();
        services.AddSingleton<IMeterAccessor, DefaultMeterAccessor>();

        // ── Metrics middleware ───────────────────────────────────────────────
        services.AddSingleton<CompanyMetricsCollector>();
        services.AddTransient<CompanyMetricsMiddleware>();

        // ── OpenTelemetry resource ───────────────────────────────────────────
        var resourceBuilder = ResourceBuilder
            .CreateDefault()
            .AddService(serviceName: serviceName, serviceVersion: serviceVersion)
            .AddTelemetrySdk();

        // ── Tracing ──────────────────────────────────────────────────────────
        if (opts.Tracing.Enabled)
        {
            services.AddOpenTelemetry()
                .WithTracing(tracing =>
                {
                    tracing.SetResourceBuilder(resourceBuilder);

                    // Sampler — AlwaysOn when ratio = 1.0, ratio-based otherwise
                    if (opts.Tracing.SampleRatio >= 1.0)
                        tracing.SetSampler(new AlwaysOnSampler());
                    else
                        tracing.SetSampler(new TraceIdRatioBasedSampler(
                            Math.Clamp(opts.Tracing.SampleRatio, 0.0, 1.0)));

                    // Always include the default Company source
                    tracing.AddSource(defaultSource);

                    // Additional custom sources
                    foreach (var src in opts.Tracing.AdditionalSources)
                        tracing.AddSource(src);

                    // Built-in instrumentation (conditional)
                    if (opts.Tracing.InstrumentAspNetCore)
                        tracing.AddAspNetCoreInstrumentation(o =>
                        {
                            o.RecordException = true;
                        });

                    if (opts.Tracing.InstrumentHttpClient)
                        tracing.AddHttpClientInstrumentation();

                    // OTLP exporter (guarded by try/catch — never crash host)
                    if (opts.Otlp.Enabled)
                    {
                        try
                        {
                            tracing.AddOtlpExporter(o =>
                            {
                                o.Endpoint = new Uri(opts.Otlp.Endpoint);
                                o.Protocol = opts.Otlp.Protocol.Equals("grpc", StringComparison.OrdinalIgnoreCase)
                                    ? OtlpExportProtocol.Grpc
                                    : OtlpExportProtocol.HttpProtobuf;
                            });
                        }
                        catch (Exception ex)
                        {
                            Trace.WriteLine($"[Company.Telemetry] Failed to configure OTLP trace exporter: {ex.Message}");
                        }
                    }
                });
        }

        // ── Metrics ──────────────────────────────────────────────────────────
        if (opts.Metrics.Enabled)
        {
            services.AddOpenTelemetry()
                .WithMetrics(metrics =>
                {
                    metrics.SetResourceBuilder(resourceBuilder);

                    // Default company meter
                    metrics.AddMeter(defaultMeter);

                    // Additional custom meters
                    foreach (var m in opts.Metrics.AdditionalMeters)
                        metrics.AddMeter(m);

                    if (opts.Metrics.InstrumentAspNetCore)
                        metrics.AddAspNetCoreInstrumentation();

                    if (opts.Metrics.InstrumentHttpClient)
                        metrics.AddHttpClientInstrumentation();

                    if (opts.Metrics.InstrumentRuntime)
                        metrics.AddRuntimeInstrumentation();

                    // OTLP metrics exporter
                    if (opts.Otlp.Enabled)
                    {
                        try
                        {
                            metrics.AddOtlpExporter(o =>
                            {
                                o.Endpoint = new Uri(opts.Otlp.Endpoint);
                                o.Protocol = opts.Otlp.Protocol.Equals("grpc", StringComparison.OrdinalIgnoreCase)
                                    ? OtlpExportProtocol.Grpc
                                    : OtlpExportProtocol.HttpProtobuf;
                            });
                        }
                        catch (Exception ex)
                        {
                            Trace.WriteLine($"[Company.Telemetry] Failed to configure OTLP metrics exporter: {ex.Message}");
                        }
                    }
                });
        }

        // ── Elastic APM agent ────────────────────────────────────────────────
        if (opts.ElasticApm.Enabled)
        {
            try
            {
                // Elastic APM is configured via environment variables or appsettings.
                // We set the well-known ElasticApm config keys programmatically.
                if (!string.IsNullOrWhiteSpace(opts.ElasticApm.ServerUrl))
                    configuration["ElasticApm:ServerUrl"] = opts.ElasticApm.ServerUrl;

                if (!string.IsNullOrWhiteSpace(opts.ElasticApm.ApiKey))
                    configuration["ElasticApm:ApiKey"] = opts.ElasticApm.ApiKey;
                else if (!string.IsNullOrWhiteSpace(opts.ElasticApm.SecretToken))
                    configuration["ElasticApm:SecretToken"] = opts.ElasticApm.SecretToken;

                configuration["ElasticApm:ServiceName"]    = serviceName;
                configuration["ElasticApm:ServiceVersion"] = serviceVersion;

                services.AddAllElasticApm();
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[Company.Telemetry] Failed to configure Elastic APM agent: {ex.Message}");
            }
        }

        return services;
    }

    /// <summary>
    /// Adds the <see cref="CompanyMetricsMiddleware"/> to the ASP.NET Core pipeline.
    /// Call this early in the middleware chain, after <c>UseCompanyLogging()</c>.
    /// </summary>
    public static IApplicationBuilder UseCompanyTelemetry(this IApplicationBuilder app)
    {
        app.UseMiddleware<CompanyMetricsMiddleware>();
        return app;
    }
}
