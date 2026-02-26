using System.Net.Http.Json;
using System.Text.Json;
using Company.Logging.Abstractions;
using Company.Logging.Serilog.AspNetCore.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Company.Logging.Serilog.AspNetCore.Reading;

/// <summary>
/// Reads structured log entries from Elasticsearch using the Query DSL.
/// Documents are expected to be in Elastic Common Schema (ECS) format,
/// as written by Elastic.Serilog.Sinks.
/// </summary>
public sealed class ElasticsearchLogReader : ILogReader
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly CompanyLoggingOptions _options;

    internal const string HttpClientName = "CompanyLogging.Reader";

    /// <summary>Initializes the reader. Registered automatically by AddCompanyLogReading().</summary>
    public ElasticsearchLogReader(
        IHttpClientFactory httpClientFactory,
        IOptions<CompanyLoggingOptions> options)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
    }

    /// <inheritdoc />
    public async Task<LogEntry> ProbeAsync(CancellationToken ct = default)
    {
        using var http = _httpClientFactory.CreateClient(HttpClientName);

        var probeId = Guid.NewGuid().ToString("N");
        var index = BuildIndexPattern(new LogQuery());

        // Data streams only support POST /_doc (auto-generated ID).
        // PUT /_doc/{id} is rejected with 400 on data streams.
        var probeDoc = new Dictionary<string, object>
        {
            ["@timestamp"]  = DateTimeOffset.UtcNow.ToString("o"),
            ["message"]     = $"[probe] write-read verification ({probeId})",
            ["log"]         = new { level = "information" },
            ["service"]     = new { name = _options.ServiceName.ToLowerInvariant().Replace(" ", "-") },
            ["deployment"]  = new { environment = _options.Environment.ToLowerInvariant() },
            ["correlation"] = new { id = probeId },
            ["labels"]      = new { probe = "true" }
        };

        // ?refresh=true forces a shard refresh so the document is immediately searchable.
        using var writeResp = await http.PostAsJsonAsync($"{index}/_doc?refresh=true", probeDoc, ct);
        writeResp.EnsureSuccessStatusCode();

        // Parse the auto-generated _id for cleanup
        using var writeBody = await writeResp.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: ct);
        var autoId = writeBody?.RootElement.TryGetProperty("_id", out var idProp) == true
            ? idProp.GetString()
            : null;

        // Read back via correlation ID
        var readResult = await QueryAsync(new LogQuery { CorrelationId = probeId, PageSize = 1 }, ct);

        // Best-effort cleanup
        if (autoId is not null)
        {
            try
            {
                using var delReq = new HttpRequestMessage(
                    HttpMethod.Delete,
                    $"{index}/_doc/{Uri.EscapeDataString(autoId)}?refresh=true");
                await http.SendAsync(delReq, ct);
            }
            catch { /* non-critical */ }
        }

        return readResult.Entries.FirstOrDefault()
            ?? throw new InvalidOperationException(
                $"Probe document (correlation.id={probeId}) was written to Elasticsearch " +
                $"(index: {index}) but could not be read back. " +
                "Check index permissions and data-stream write access.");
    }

    /// <inheritdoc />
    public async Task<LogQueryResult> QueryAsync(LogQuery query, CancellationToken ct = default)
    {
        using var http = _httpClientFactory.CreateClient(HttpClientName);

        var index = BuildIndexPattern(query);
        var body = BuildSearchBody(query);

        using var response = await http.PostAsJsonAsync($"{index}/_search", body, cancellationToken: ct);

        // 404 means the index/data-stream doesn't exist yet (service has never written a log).
        // Return an empty result instead of throwing — callers should not have to guard for this.
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return new LogQueryResult
            {
                Entries    = new List<LogEntry>(),
                TotalCount = 0,
                PageIndex  = query.PageIndex,
                PageSize   = query.PageSize
            };
        }

        response.EnsureSuccessStatusCode();

        using var doc = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: ct);
        return ParseResponse(doc!, query);
    }

    // ── Index pattern ──────────────────────────────────────────────────────────

    private string BuildIndexPattern(LogQuery query)
    {
        var service = (query.ServiceName ?? _options.ServiceName)
            .ToLowerInvariant().Replace(" ", "-");
        var env = (query.Environment ?? _options.Environment)
            .ToLowerInvariant();
        return $"logs-{service}-{env}";
    }

    // ── ES Query DSL ──────────────────────────────────────────────────────────

    private static object BuildSearchBody(LogQuery query)
    {
        var filters = new List<object>();

        var from = query.From ?? DateTimeOffset.UtcNow.AddDays(-1);
        var to   = query.To   ?? DateTimeOffset.UtcNow;

        // Use Dictionary<string, object> so "@timestamp" is preserved literally in JSON.
        // Anonymous type @timestamp serialises as "timestamp" (@ is a C# keyword-escape prefix only).
        filters.Add(new Dictionary<string, object>
        {
            ["range"] = new Dictionary<string, object>
            {
                ["@timestamp"] = new
                {
                    gte = from.UtcDateTime.ToString("o"),
                    lte = to.UtcDateTime.ToString("o")
                }
            }
        });

        if (query.MinLevel.HasValue)
        {
            filters.Add(new
            {
                terms = new Dictionary<string, string[]>
                {
                    ["log.level"] = LevelsAtOrAbove(query.MinLevel.Value)
                }
            });
        }

        if (query.CorrelationId is not null)
            filters.Add(Term("correlation.id", query.CorrelationId));

        if (query.TraceId is not null)
            filters.Add(Term("trace.id", query.TraceId));

        object queryClause = query.SearchText is not null
            ? new
            {
                @bool = new
                {
                    filter = filters,
                    must = new[]
                    {
                        new { match = new Dictionary<string, string> { ["message"] = query.SearchText } }
                    }
                }
            }
            : new { @bool = new { filter = filters } };

        // Sort also needs "@timestamp" as a literal key — use Dictionary again.
        var sort = new[]
        {
            new Dictionary<string, object>
            {
                ["@timestamp"] = new { order = "desc" }
            }
        };

        return new
        {
            from  = query.PageIndex * query.PageSize,
            size  = Math.Min(query.PageSize, 1000),
            sort,
            query = queryClause,
            track_total_hits = true
        };
    }

    private static object Term(string field, string value) =>
        new { term = new Dictionary<string, string> { [field] = value } };

    // ── Level mapping ─────────────────────────────────────────────────────────

    private static readonly string[] AllLevels =
        ["verbose", "debug", "information", "warning", "error", "fatal"];

    private static string[] LevelsAtOrAbove(LogLevel minLevel)
    {
        var startIndex = minLevel switch
        {
            LogLevel.Trace       => 0,
            LogLevel.Debug       => 1,
            LogLevel.Information => 2,
            LogLevel.Warning     => 3,
            LogLevel.Error       => 4,
            LogLevel.Critical    => 5,
            _                    => 2
        };
        return AllLevels[startIndex..];
    }

    // ── Response parsing ──────────────────────────────────────────────────────

    private static LogQueryResult ParseResponse(JsonDocument doc, LogQuery query)
    {
        var hits  = doc.RootElement.GetProperty("hits");
        var total = hits.GetProperty("total").GetProperty("value").GetInt64();
        var entries = hits.GetProperty("hits")
            .EnumerateArray()
            .Select(hit => MapEntry(hit.GetProperty("_source")))
            .ToList();

        return new LogQueryResult
        {
            Entries    = entries,
            TotalCount = total,
            PageIndex  = query.PageIndex,
            PageSize   = query.PageSize
        };
    }

    private static LogEntry MapEntry(JsonElement src)
    {
        var extra = new Dictionary<string, string?>();

        foreach (var prop in src.EnumerateObject())
        {
            if (!MappedTopLevelKeys.Contains(prop.Name))
                FlattenIntoExtra(extra, prop.Name, prop.Value);
        }

        return new LogEntry
        {
            Timestamp      = ParseTimestamp(src),
            Level          = GetNested(src, "log", "level") ?? string.Empty,
            Message        = GetString(src, "message") ?? string.Empty,
            ServiceName    = GetNested(src, "service", "name"),
            ServiceVersion = GetNested(src, "service", "version"),
            InstanceId     = GetNested(src, "service", "instance", "id"),
            Environment    = GetNested(src, "deployment", "environment"),
            CorrelationId  = GetNested(src, "correlation", "id"),
            TraceId        = GetNested(src, "trace", "id"),
            SpanId         = GetNested(src, "span", "id"),
            UserId         = GetNested(src, "user", "id"),
            EventAction    = GetNested(src, "event", "action"),
            EntityType     = GetString(src, "entity.type"),
            EntityId       = GetString(src, "entity.id"),
            Extra          = extra
        };
    }

    private static DateTimeOffset ParseTimestamp(JsonElement src)
    {
        if (src.TryGetProperty("@timestamp", out var ts) &&
            DateTimeOffset.TryParse(ts.GetString(), out var dto))
            return dto;
        return DateTimeOffset.MinValue;
    }

    private static string? GetNested(JsonElement el, params string[] path)
    {
        var current = el;
        foreach (var key in path)
        {
            if (!current.TryGetProperty(key, out current))
                return null;
        }
        return current.ValueKind == JsonValueKind.String ? current.GetString() : null;
    }

    private static string? GetString(JsonElement el, string key)
        => el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    private static void FlattenIntoExtra(Dictionary<string, string?> target, string prefix, JsonElement el)
    {
        switch (el.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var child in el.EnumerateObject())
                    FlattenIntoExtra(target, $"{prefix}.{child.Name}", child.Value);
                break;
            case JsonValueKind.String:
                target[prefix] = el.GetString();
                break;
            default:
                target[prefix] = el.ToString();
                break;
        }
    }

    private static readonly HashSet<string> MappedTopLevelKeys =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "@timestamp", "log", "message", "service", "deployment",
            "correlation", "trace", "span", "user", "event",
            "entity.type", "entity.id"
        };
}