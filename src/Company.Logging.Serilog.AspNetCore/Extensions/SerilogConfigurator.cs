using Company.Logging.Serilog.AspNetCore.Configuration;
using Company.Logging.Serilog.AspNetCore.Enrichers;
using Company.Logging.Serilog.AspNetCore.Filters;
using Elastic.CommonSchema.Serilog;
using Elastic.Ingest.Elasticsearch;
using Elastic.Ingest.Elasticsearch.DataStreams;
using Elastic.Serilog.Sinks;
using Elastic.Transport;
using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Events;

namespace Company.Logging.Serilog.AspNetCore.Extensions;

/// <summary>
/// Builds the Serilog <see cref="LoggerConfiguration"/> from <see cref="CompanyLoggingOptions"/>.
/// Kept separate so it can be unit-tested and reused outside of the full DI pipeline.
/// </summary>
public static class SerilogConfigurator
{
    /// <summary>
    /// Applies the full Company logging pipeline to a <see cref="LoggerConfiguration"/>.
    /// </summary>
    public static LoggerConfiguration Apply(
        LoggerConfiguration loggerConfig,
        CompanyLoggingOptions options,
        IConfiguration? configuration = null)
    {
        // ── Minimum levels ────────────────────────────────────────────────────
        loggerConfig
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
            .MinimumLevel.Override("System", LogEventLevel.Warning);

        // Allow override from standard Serilog section if present
        if (configuration is not null)
            loggerConfig.ReadFrom.Configuration(configuration);

        // ── Enrichers ─────────────────────────────────────────────────────────
        loggerConfig
            .Enrich.FromLogContext()
            .Enrich.WithMachineName()
            .Enrich.WithEnvironmentName()
            .Enrich.WithProcessId()
            .Enrich.WithThreadId()
            .Enrich.With<TraceEnricher>()
            .Enrich.With<CorrelationEnricher>();

        // ── Redaction ─────────────────────────────────────────────────────────
        loggerConfig.Enrich.With(new RedactionPolicy(options.Redaction));

        // ── Sampling ──────────────────────────────────────────────────────────
        var sampler = new SamplingFilter(options.Sampling);
        loggerConfig.Filter.ByIncludingOnly(e => sampler.IsEnabled(e));

        // ── Sinks ─────────────────────────────────────────────────────────────
        void ConfigureSinks(global::Serilog.Configuration.LoggerSinkConfiguration sink)
        {
            // ECS JSON to stdout — always on; container aggregators pick this up
            sink.Console(
                formatter: new EcsTextFormatter(),
                standardErrorFromLevel: LogEventLevel.Error);

            if (options.Elasticsearch.Enabled)
                ConfigureElasticsearchSink(sink, options);
        }

        if (options.Async.Enabled)
        {
            loggerConfig.WriteTo.Async(
                ConfigureSinks,
                bufferSize: options.Async.BufferSize,
                blockWhenFull: options.Async.BlockWhenFull);
        }
        else
        {
            ConfigureSinks(loggerConfig.WriteTo);
        }

        return loggerConfig;
    }

    private static void ConfigureElasticsearchSink(
        global::Serilog.Configuration.LoggerSinkConfiguration sink,
        CompanyLoggingOptions options)
    {
        var es = options.Elasticsearch;
        var serviceName = options.ServiceName.ToLowerInvariant().Replace(" ", "-");

        try
        {
            sink.Elasticsearch(
                nodes: new[] { new Uri(es.Uri) },
                opts =>
                {
                    opts.DataStream = new DataStreamName("logs", serviceName, options.Environment);

                    opts.BootstrapMethod = es.AutoRegisterTemplate
                        ? BootstrapMethod.Failure
                        : BootstrapMethod.None;

                    opts.ConfigureChannel = channelOpts =>
                    {
                        channelOpts.BufferOptions = new Elastic.Channels.BufferOptions
                        {
                            OutboundBufferMaxSize = es.BatchPostingLimit,
                            ExportMaxRetries = 3,
                            OutboundBufferMaxLifetime = TimeSpan.FromSeconds(es.PeriodSeconds),
                        };
                    };
                },
                // Configure transport auth via callback (URI-based for all modes).
                // For Elastic Cloud, set Uri to your cloud HTTPS endpoint.
                transport =>
                {
                    if (!string.IsNullOrWhiteSpace(es.ApiKey))
                        transport.Authentication(new ApiKey(es.ApiKey));
                    else if (!string.IsNullOrWhiteSpace(es.Username) && !string.IsNullOrWhiteSpace(es.Password))
                        transport.Authentication(new BasicAuthentication(es.Username, es.Password));
                });
        }
        catch (Exception ex)
        {
            // Never crash the app on a bad ES configuration — fall back to console only
            global::Serilog.Debugging.SelfLog.WriteLine(
                $"[Company.Logging] Failed to configure Elasticsearch sink: {ex.Message}. Falling back to console only.");
        }
    }
}
