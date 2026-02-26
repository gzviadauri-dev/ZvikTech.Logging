using System.Net.Http.Headers;
using System.Text;
using Company.Logging.Abstractions;
using Company.Logging.Serilog.AspNetCore.Configuration;
using Company.Logging.Serilog.AspNetCore.Enrichers;
using Company.Logging.Serilog.AspNetCore.Middleware;
using Company.Logging.Serilog.AspNetCore.Reading;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Serilog;
using Serilog.Events;

namespace Company.Logging.Serilog.AspNetCore.Extensions;

/// <summary>
/// Extension methods to integrate Company structured logging into a .NET 8 application.
/// </summary>
public static class CompanyLoggingExtensions
{
    /// <summary>
    /// Registers Company logging on the <see cref="IHostBuilder"/>.
    /// Call this <b>before</b> <c>Build()</c> so the bootstrap logger catches startup errors.
    /// </summary>
    /// <example>
    /// <code>
    /// var builder = WebApplication.CreateBuilder(args);
    /// builder.Host.UseCompanySerilog(builder.Configuration);
    /// </code>
    /// </example>
    public static IHostBuilder UseCompanySerilog(
        this IHostBuilder hostBuilder,
        IConfiguration configuration,
        Action<CompanyLoggingOptions>? configureOptions = null)
    {
        // Bind options eagerly so we can configure the bootstrap logger
        var options = new CompanyLoggingOptions();
        configuration.GetSection(CompanyLoggingOptions.SectionName).Bind(options);
        configureOptions?.Invoke(options);

        // Bootstrap logger: catches exceptions during host startup
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Warning()
            .Enrich.FromLogContext()
            .WriteTo.Console(outputTemplate:
                "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateBootstrapLogger();

        hostBuilder.UseSerilog((context, services, loggerConfig) =>
        {
            // Re-resolve options so IOptions pattern is honoured
            var resolvedOptions = services.GetService<IOptions<CompanyLoggingOptions>>()?.Value ?? options;
            SerilogConfigurator.Apply(loggerConfig, resolvedOptions, context.Configuration);

            // Service and environment enrichers require DI (IOptions)
            loggerConfig.Enrich.With(services.GetRequiredService<ServiceEnricher>());
            loggerConfig.Enrich.With(services.GetRequiredService<EnvironmentEnricher>());
            loggerConfig.Enrich.With(services.GetRequiredService<ActorEnricher>());
        });

        return hostBuilder;
    }

    /// <summary>
    /// Registers Company logging services into the DI container.
    /// Must be called in <c>Program.cs</c> service registration.
    /// </summary>
    public static IServiceCollection AddCompanyLogging(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<CompanyLoggingOptions>? configureOptions = null)
    {
        // Register and bind options
        services
            .AddOptions<CompanyLoggingOptions>()
            .Bind(configuration.GetSection(CompanyLoggingOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        if (configureOptions is not null)
        {
            services.Configure(configureOptions);
        }

        // HttpContextAccessor required by ActorEnricher and ICorrelationContext
        services.AddHttpContextAccessor();

        // Serilog enrichers as singletons (safe: they are stateless or use IHttpContextAccessor)
        services.AddSingleton<ServiceEnricher>();
        services.AddSingleton<EnvironmentEnricher>();
        services.AddSingleton<TraceEnricher>();
        services.AddSingleton<CorrelationEnricher>();
        services.AddSingleton<ActorEnricher>();

        // Middleware (registered as transient because IMiddleware factory pattern)
        services.AddTransient<CorrelationMiddleware>();

        // Abstractions available for application code
        services.AddScoped<ICorrelationContext, HttpContextCorrelationContextAccessor>();
        services.AddScoped<ILogDataScope, LogDataScope>();

        return services;
    }

    /// <summary>
    /// Registers Company logging middleware into the ASP.NET Core pipeline.
    /// Call this early in <c>app.Use...</c> chain, ideally before routing.
    /// </summary>
    public static IApplicationBuilder UseCompanyLogging(
        this IApplicationBuilder app,
        Action<CompanyLoggingOptions>? configure = null)
    {
        // Correlation middleware must run before request logging
        app.UseMiddleware<CorrelationMiddleware>();

        // Snapshot resolved options into a local copy so we never mutate the IOptions singleton
        var resolved = app.ApplicationServices
            .GetService<IOptions<CompanyLoggingOptions>>()?.Value
            ?? new CompanyLoggingOptions();

        var options = new CompanyLoggingOptions
        {
            ServiceName = resolved.ServiceName,
            ServiceVersion = resolved.ServiceVersion,
            Environment = resolved.Environment,
            Region = resolved.Region,
            CorrelationHeader = resolved.CorrelationHeader,
            EnableRequestLogging = resolved.EnableRequestLogging,
            EnableBodyLogging = resolved.EnableBodyLogging,
            BodySizeLimitBytes = resolved.BodySizeLimitBytes,
            RequestLoggingExcludedPaths = resolved.RequestLoggingExcludedPaths,
            RequestLoggingWarnAboveStatus = resolved.RequestLoggingWarnAboveStatus,
            RequestLoggingErrorAboveStatus = resolved.RequestLoggingErrorAboveStatus,
            Redaction = resolved.Redaction,
            Sampling = resolved.Sampling,
            Elasticsearch = resolved.Elasticsearch,
            Async = resolved.Async,
            MetaKeyWhitelist = resolved.MetaKeyWhitelist,
        };

        configure?.Invoke(options);

        if (options.EnableRequestLogging)
        {
            var excludedPaths = options.RequestLoggingExcludedPaths
                .Select(p => p.ToLowerInvariant())
                .ToHashSet();

            app.UseSerilogRequestLogging(requestOptions =>
            {
                requestOptions.MessageTemplate =
                    "HTTP {http.method} {http.route} responded {http.status_code} in {elapsed_ms:0.0000} ms";

                // Enrich with ECS-compatible HTTP fields + correlation
                requestOptions.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
                {
                    diagnosticContext.Set("http.method", httpContext.Request.Method);
                    diagnosticContext.Set("http.route",
                        httpContext.GetEndpoint()?.DisplayName
                        ?? httpContext.Request.Path.Value
                        ?? "unknown");
                    diagnosticContext.Set("http.scheme", httpContext.Request.Scheme);
                    diagnosticContext.Set("http.host", httpContext.Request.Host.Value);
                    diagnosticContext.Set("url.path", httpContext.Request.Path);
                    diagnosticContext.Set("client.ip",
                        httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown");

                    // correlation.id already in LogContext via CorrelationMiddleware, but explicit for clarity
                    if (CorrelationContext.CorrelationId is { } cid)
                        diagnosticContext.Set("correlation.id", cid);
                };

                // Dynamic log level based on status code
                requestOptions.GetLevel = (httpContext, elapsed, ex) =>
                {
                    var path = httpContext.Request.Path.Value?.ToLowerInvariant() ?? string.Empty;

                    // Excluded paths use Verbose (effectively suppressed)
                    if (excludedPaths.Any(p => path.StartsWith(p, StringComparison.Ordinal)))
                        return LogEventLevel.Verbose;

                    if (ex is not null) return LogEventLevel.Error;

                    var status = httpContext.Response.StatusCode;
                    if (status >= options.RequestLoggingErrorAboveStatus) return LogEventLevel.Error;
                    if (status >= options.RequestLoggingWarnAboveStatus) return LogEventLevel.Warning;
                    return LogEventLevel.Information;
                };
            });
        }

        // Graceful shutdown flush - ensure logs are written before process exits
        var lifetime = app.ApplicationServices.GetService<IHostApplicationLifetime>();
        lifetime?.ApplicationStopped.Register(() =>
        {
            Log.Information("Application stopped. Flushing logs...");
            Log.CloseAndFlush();
        });

        return app;
    }

    /// <summary>
    /// Registers <see cref="ILogReader"/> so you can query log entries back from Elasticsearch.
    /// Call this in addition to <see cref="AddCompanyLogging"/> when you need read access.
    /// Requires <c>Elasticsearch.Enabled = true</c> and a reachable ES cluster.
    /// </summary>
    /// <example>
    /// <code>
    /// builder.Services.AddCompanyLogging(builder.Configuration);
    /// builder.Services.AddCompanyLogReading(builder.Configuration);
    ///
    /// // Inject anywhere:
    /// app.MapGet("/logs", async (ILogReader reader) =>
    /// {
    ///     var result = await reader.QueryAsync(new LogQuery
    ///     {
    ///         MinLevel   = LogLevel.Warning,
    ///         From       = DateTimeOffset.UtcNow.AddHours(-1),
    ///         SearchText = "payment"
    ///     });
    ///     return result;
    /// });
    /// </code>
    /// </example>
    public static IServiceCollection AddCompanyLogReading(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<CompanyLoggingOptions>? configureOptions = null)
    {
        // Register options if not already done (safe to call multiple times)
        services
            .AddOptions<CompanyLoggingOptions>()
            .Bind(configuration.GetSection(CompanyLoggingOptions.SectionName));

        if (configureOptions is not null)
            services.Configure(configureOptions);

        // Named HttpClient — base address, auth headers, and timeout configured from options
        services.AddHttpClient(ElasticsearchLogReader.HttpClientName, (sp, client) =>
        {
            var opts = sp.GetRequiredService<IOptions<CompanyLoggingOptions>>().Value;
            var es   = opts.Elasticsearch;

            client.BaseAddress = new Uri(es.Uri.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));

            if (!string.IsNullOrWhiteSpace(es.ApiKey))
            {
                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("ApiKey", es.ApiKey);
            }
            else if (!string.IsNullOrWhiteSpace(es.Username) && !string.IsNullOrWhiteSpace(es.Password))
            {
                var credentials = Convert.ToBase64String(
                    Encoding.UTF8.GetBytes($"{es.Username}:{es.Password}"));
                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Basic", credentials);
            }
        });

        services.AddScoped<ILogReader, ElasticsearchLogReader>();

        return services;
    }
}
