using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace Company.Logging.Serilog.AspNetCore.Configuration;

/// <summary>Root configuration section: "CompanyLogging"</summary>
public sealed class CompanyLoggingOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "CompanyLogging";

    /// <summary>Logical service name. Used in log fields and ES index. E.g. "orders-api".</summary>
    [Required(AllowEmptyStrings = false)]
    public string ServiceName { get; set; } = "unknown-service";

    /// <summary>Service version. Defaults to entry assembly version.</summary>
    public string? ServiceVersion { get; set; }

    /// <summary>Deployment environment: prod | stage | dev</summary>
    [Required(AllowEmptyStrings = false)]
    public string Environment { get; set; } = "dev";

    /// <summary>Optional cloud/datacenter region. E.g. "us-east-1".</summary>
    public string? Region { get; set; }

    /// <summary>HTTP header name for correlation ID propagation.</summary>
    [Required(AllowEmptyStrings = false)]
    public string CorrelationHeader { get; set; } = "X-Correlation-Id";

    /// <summary>Log one completion line per HTTP request.</summary>
    public bool EnableRequestLogging { get; set; } = true;

    /// <summary>Log request/response bodies. DISABLED by default for security.</summary>
    public bool EnableBodyLogging { get; set; } = false;

    /// <summary>Max body size to log when body logging is enabled.</summary>
    [Range(1, 1_048_576)]
    public int BodySizeLimitBytes { get; set; } = 4096;

    /// <summary>Paths excluded from request logging (prefix match). Default: /health, /metrics, /favicon.ico.</summary>
    public List<string> RequestLoggingExcludedPaths { get; set; } = new() { "/health", "/metrics", "/favicon.ico" };

    /// <summary>Minimum status code to log as a Warning (inclusive). Default: 400.</summary>
    [Range(100, 599)]
    public int RequestLoggingWarnAboveStatus { get; set; } = 400;

    /// <summary>Minimum status code to log as an Error (inclusive). Default: 500.</summary>
    [Range(100, 599)]
    public int RequestLoggingErrorAboveStatus { get; set; } = 500;

    /// <summary>Redaction settings.</summary>
    [ValidateObjectMembers]
    public RedactionOptions Redaction { get; set; } = new();

    /// <summary>Sampling settings.</summary>
    [ValidateObjectMembers]
    public SamplingOptions Sampling { get; set; } = new();

    /// <summary>Elasticsearch sink settings.</summary>
    [ValidateObjectMembers]
    public ElasticsearchOptions Elasticsearch { get; set; } = new();

    /// <summary>Async wrapper settings.</summary>
    [ValidateObjectMembers]
    public AsyncOptions Async { get; set; } = new();

    /// <summary>Whitelist of Meta keys allowed to be logged. Empty = allow all.</summary>
    public List<string> MetaKeyWhitelist { get; set; } = new();
}

/// <summary>Controls PII and secrets redaction.</summary>
public sealed class RedactionOptions
{
    /// <summary>Keys whose values will be replaced with "***". Case-insensitive.</summary>
    public List<string> SensitiveKeys { get; set; } = new()
    {
        "password", "passwd", "secret", "token", "apikey", "api_key",
        "authorization", "auth", "credential", "credentials",
        "connectionstring", "connection_string", "privatekey", "private_key",
        "clientsecret", "client_secret"
    };

    /// <summary>Replacement value for sensitive data.</summary>
    public string RedactedValue { get; set; } = "***";
}

/// <summary>Controls log sampling to reduce volume.</summary>
public sealed class SamplingOptions
{
    /// <summary>Enable sampling of successful (2xx) request log events.</summary>
    public bool SampleSuccessRequests { get; set; } = false;

    /// <summary>Keep 1 in N successful request events (e.g. 10 = 10%).</summary>
    [Range(1, int.MaxValue)]
    public int SuccessSampleRate { get; set; } = 10;
}

/// <summary>Elasticsearch sink configuration.</summary>
public sealed class ElasticsearchOptions
{
    /// <summary>Enable the Elasticsearch sink.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Elasticsearch node URI. E.g. "http://localhost:9200".</summary>
    [Required(AllowEmptyStrings = false)]
    public string Uri { get; set; } = "http://localhost:9200";

    /// <summary>Auto-register ILM-aware index template on startup.</summary>
    public bool AutoRegisterTemplate { get; set; } = true;

    /// <summary>Number of log events per batch POST.</summary>
    [Range(1, 10_000)]
    public int BatchPostingLimit { get; set; } = 1000;

    /// <summary>How often to flush the batch (seconds).</summary>
    [Range(1, 300)]
    public int PeriodSeconds { get; set; } = 2;

    /// <summary>Optional Basic Auth username.</summary>
    public string? Username { get; set; }

    /// <summary>Optional Basic Auth password. Will not be logged.</summary>
    public string? Password { get; set; }

    /// <summary>Optional API key for Elastic Cloud authentication.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Optional Elastic Cloud ID.</summary>
    public string? CloudId { get; set; }

    /// <summary>Path to write a durable disk buffer when ES is unreachable. Null = disabled.</summary>
    public string? DurableBufferPath { get; set; }
}

/// <summary>Async wrapper configuration to prevent sink I/O from blocking request threads.</summary>
public sealed class AsyncOptions
{
    /// <summary>Wrap all sinks in WriteTo.Async.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>In-memory buffer size (events). Overflow policy = drop oldest.</summary>
    [Range(100, 1_000_000)]
    public int BufferSize { get; set; } = 10_000;

    /// <summary>Block caller when buffer is full (true) or drop events (false). Default: drop.</summary>
    public bool BlockWhenFull { get; set; } = false;
}
