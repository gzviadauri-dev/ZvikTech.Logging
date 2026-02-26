# Company.Logging

> **Production-ready structured logging for .NET 8 microservices.**  
> Serilog + Elasticsearch (ECS) · PII redaction · Correlation IDs · OpenTelemetry traces · Graceful degradation

[![CI](https://github.com/gzviadauri-dev/ZvikTech.Logging/actions/workflows/ci.yml/badge.svg)](https://github.com/gzviadauri-dev/ZvikTech.Logging/actions)
[![NuGet](https://img.shields.io/nuget/v/Company.Logging.Serilog.AspNetCore)](https://www.nuget.org/packages/Company.Logging.Serilog.AspNetCore)

---

## Table of Contents

- [Why this library?](#why-this-library)
- [Architecture](#architecture)
- [Packages](#packages)
- [Quick Start](#quick-start)
- [Configuration Reference](#configuration-reference)
- [Features In Depth](#features-in-depth)
- [Elasticsearch Sink](#elasticsearch-sink)
- [Running the Sample](#running-the-sample)
- [Publishing to NuGet](#publishing-to-nuget)
- [FAQ / Troubleshooting](#faq--troubleshooting)

---

## Why this library?

Logging across microservices typically suffers from:

| Problem | This library's answer |
|---|---|
| Each team configures Serilog differently | One `UseCompanySerilog()` call wires everything |
| PII/secrets leaking into logs | Redaction policy enabled by default |
| Random object dumps causing Elasticsearch mapping explosions | `LogData` model with `Tags`/`Meta` whitelisting |
| Latency spikes from synchronous ES writes | `WriteTo.Async` with bounded queue + drop policy |
| App crashes when ES is unavailable | Try/catch in sink config; fallback to console JSON |
| Disconnected traces across services | W3C `trace.id`/`span.id` from `Activity` + `X-Correlation-Id` propagation |
| Different log shapes per service | ECS (Elastic Common Schema) field names everywhere |

---

## Architecture

```
┌─────────────────────────────────────────────────────────────┐
│  Your Microservice (ASP.NET Core)                           │
│                                                             │
│  Program.cs                                                 │
│  ├── builder.Host.UseCompanySerilog(config)   ← bootstrap  │
│  ├── services.AddCompanyLogging(config)       ← DI setup   │
│  └── app.UseCompanyLogging()                  ← middleware  │
│                                                             │
│  Middleware Pipeline:                                       │
│  CorrelationMiddleware → SerilogRequestLogging → Endpoint  │
│                                                             │
│  Enrichers (run per log event):                            │
│  ServiceEnricher · EnvironmentEnricher · TraceEnricher     │
│  CorrelationEnricher · ActorEnricher                       │
│                                                             │
│  Filters:                                                   │
│  RedactionPolicy · SamplingFilter                          │
└──────────────────────────────┬──────────────────────────────┘
                               │ Serilog Pipeline (async)
              ┌────────────────┴────────────────┐
              ▼                                  ▼
   Console (JSON/ECS)              Elasticsearch (ECS via
   stdout — always on              Elastic.Serilog.Sinks)
                                   Data Stream: logs-{service}-{env}
```

---

## Packages

| Package | Description |
|---|---|
| `Company.Logging.Abstractions` | `LogData`, `ICorrelationContext`, `ILogDataScope` — framework-agnostic, no Serilog dependency |
| `Company.Logging.Serilog.AspNetCore` | Full implementation: enrichers, middleware, ES sink configuration, extension methods |

---

## Quick Start

### 1. Install packages

```bash
dotnet add package Company.Logging.Serilog.AspNetCore
```

### 2. Configure `Program.cs`

```csharp
var builder = WebApplication.CreateBuilder(args);

// Register logging infrastructure
builder.Host.UseCompanySerilog(builder.Configuration);
builder.Services.AddCompanyLogging(builder.Configuration);

var app = builder.Build();

// Add middleware: correlation IDs + request logging
app.UseCompanyLogging();

app.MapGet("/", () => "Hello World!");
app.Run();
```

### 3. Add `appsettings.json`

```json
{
  "CompanyLogging": {
    "ServiceName": "my-orders-api",
    "Environment": "prod",
    "Elasticsearch": {
      "Enabled": true,
      "Uri": "http://your-es-cluster:9200"
    }
  }
}
```

### 4. Inject and use in your services

```csharp
public class OrderService
{
    private readonly ILogger<OrderService> _logger;
    private readonly ILogDataScope _logScope;

    public OrderService(ILogger<OrderService> logger, ILogDataScope logScope)
    {
        _logger = logger;
        _logScope = logScope;
    }

    public async Task CreateOrder(string orderId, string customerId)
    {
        var logData = LogData.Create()
            .WithEvent("order.created")
            .WithEntity("Order", orderId)
            .WithTag("customer.segment", "premium");

        using var scope = _logScope.BeginScope(logData);

        _logger.LogInformation("Creating order {OrderId} for {CustomerId}", orderId, customerId);
        // Structured log will include: event.action, entity.type, entity.id, tag.customer.segment
    }
}
```

---

## Configuration Reference

All settings live under `"CompanyLogging"` in `appsettings.json`.

```json
{
  "CompanyLogging": {
    "ServiceName": "orders-api",
    "ServiceVersion": "1.2.3",
    "Environment": "prod",
    "Region": "us-east-1",
    "CorrelationHeader": "X-Correlation-Id",
    "EnableRequestLogging": true,
    "EnableBodyLogging": false,
    "BodySizeLimitBytes": 4096,
    "RequestLoggingExcludedPaths": ["/health", "/metrics"],
    "RequestLoggingWarnAboveStatus": 400,
    "RequestLoggingErrorAboveStatus": 500,

    "Redaction": {
      "SensitiveKeys": ["password", "token", "secret", "apikey", "authorization"],
      "RedactedValue": "***"
    },

    "Sampling": {
      "SampleSuccessRequests": false,
      "SuccessSampleRate": 10
    },

    "Elasticsearch": {
      "Enabled": true,
      "Uri": "http://localhost:9200",
      "AutoRegisterTemplate": true,
      "BatchPostingLimit": 1000,
      "PeriodSeconds": 2,
      "Username": "",
      "Password": "",
      "ApiKey": "",
      "CloudId": "",
      "DurableBufferPath": null
    },

    "Async": {
      "Enabled": true,
      "BufferSize": 10000,
      "BlockWhenFull": false
    },

    "MetaKeyWhitelist": []
  }
}
```

### Environment Variables (override for containers/k8s)

```
CompanyLogging__ServiceName=payments-api
CompanyLogging__Environment=prod
CompanyLogging__Elasticsearch__Uri=http://es-cluster:9200
CompanyLogging__Elasticsearch__ApiKey=your-api-key
```

---

## Features In Depth

### Correlation IDs

Every request gets a `correlation.id`. If the caller sends `X-Correlation-Id: abc-123`, that value is preserved. Otherwise a new GUID is generated. The ID is:
- Added to the Serilog `LogContext` for all log events in the request
- Forwarded on the response as `X-Correlation-Id`
- Available via DI: `ICorrelationContext.CorrelationId`

```csharp
public class MyController
{
    private readonly ICorrelationContext _correlation;

    public MyController(ICorrelationContext correlation)
    {
        _correlation = correlation;
    }

    public IActionResult Get()
    {
        // Use correlation ID to pass downstream (HTTP clients, message buses)
        var id = _correlation.CorrelationId;
        httpClient.DefaultRequestHeaders.Add("X-Correlation-Id", id);
        // ...
    }
}
```

### PII Redaction

By default, any log property whose name matches a sensitive key (case-insensitive) is replaced with `***`. This protects against accidental logging of:

```csharp
// ❌ This would log password=*** instead of the actual value
logger.LogDebug("User login: {Username} / {Password}", username, password);

// ✅ Preferred: don't pass sensitive data at all
logger.LogDebug("User login attempt for {Username}", username);
```

**Add custom keys to redact:**
```json
"Redaction": {
  "SensitiveKeys": ["password", "ssn", "creditcard", "iban"]
}
```

### OpenTelemetry Trace Correlation

The `TraceEnricher` reads `Activity.Current` (W3C trace context) and adds:
- `trace.id` — 32-char hex trace ID
- `span.id` — 16-char hex span ID
- `transaction.id` — mirrors `span.id` (Elastic APM convention)

This means your Kibana logs automatically link to Elastic APM traces with zero extra code.

### Structured LogData (avoid mapping explosions)

Never do this — it will cause Elasticsearch mapping conflicts:

```csharp
// ❌ Bad: dumps arbitrary object, pollutes ES mappings
logger.LogInformation("Order: {@Order}", complexOrderObject);
```

Use `LogData` instead:

```csharp
// ✅ Good: controlled, whitelisted fields
var data = LogData.Create()
    .WithEvent("order.shipped")
    .WithEntity("Order", orderId)
    .WithTag("shipping.carrier", "FedEx")
    .WithTag("shipping.priority", "express");

using var scope = logScope.BeginScope(data);
logger.LogInformation("Order {OrderId} shipped", orderId);
```

### Graceful Degradation

When Elasticsearch is unavailable:
1. The async buffer absorbs bursts (default: 10,000 events)
2. When the buffer fills, **events are dropped** (not blocking — `BlockWhenFull: false`)
3. Console JSON sink **always** remains active as fallback
4. ES sink configuration errors are caught and logged to `SelfLog` — **the app never crashes**
5. Set `DurableBufferPath` for disk-based durability that replays when ES recovers

### Request Logging

One structured log line per request with ECS fields:

```
HTTP POST /orders responded 201 in 47.3ms
  http.method: POST
  http.route: /orders (endpoint display name)
  http.status_code: 201
  elapsed_ms: 47.3
  correlation.id: abc-123
  trace.id: 4bf92f3577b34da6a3ce929d0e0e4736
```

Health check and metrics paths are excluded by default.

---

## Elasticsearch Sink

This library uses **`Elastic.Serilog.Sinks`** — the official sink maintained by Elastic, not the abandoned community `Serilog.Sinks.Elasticsearch`.

**Why `Elastic.Serilog.Sinks`:**
- Maintained by Elastic (same team as Elasticsearch)
- Writes natively in **Elastic Common Schema (ECS)** format
- Targets Elasticsearch **Data Streams** (modern index management)
- Built-in ILM template registration
- Supports Elastic Cloud, API keys, Basic Auth
- Batching with configurable retry

**Authentication options:**

```json
// Option A: No auth (local dev)
"Elasticsearch": { "Uri": "http://localhost:9200" }

// Option B: Basic Auth (on-prem)
"Elasticsearch": { "Uri": "https://es:9200", "Username": "elastic", "Password": "..." }

// Option C: API Key (recommended for production)
"Elasticsearch": { "Uri": "https://es:9200", "ApiKey": "your-base64-api-key" }

// Option D: Elastic Cloud
"Elasticsearch": { "CloudId": "my-cluster:...", "ApiKey": "your-api-key" }
```

### Kibana Setup

After starting docker-compose:

1. Open Kibana: `http://localhost:5601`
2. Go to **Stack Management → Data Views**
3. Create data view: `logs-*` with `@timestamp` as the time field
4. Go to **Discover** — your logs appear immediately

---

## Running the Sample

### Prerequisites

- Docker Desktop
- .NET 8 SDK

### Start Elasticsearch + Kibana

```bash
cd Company.Logging
docker-compose up -d elasticsearch kibana
# Wait ~30s for ES to be healthy
```

### Run the sample API

```bash
cd src/SampleMicroservice
dotnet run
# API available at http://localhost:5000
# Swagger UI at http://localhost:5000/swagger
```

### Test the endpoints

```bash
# Create an order (with correlation ID)
curl -X POST http://localhost:5000/orders \
  -H "Content-Type: application/json" \
  -H "X-Correlation-Id: my-test-123" \
  -d '{"customerId": "cust-1", "items": ["item-a", "item-b"]}'

# Trigger exception logging
curl http://localhost:5000/orders/error-demo

# Check health
curl http://localhost:5000/health
```

### View logs in Kibana

1. Open `http://localhost:5601`
2. **Discover** → Select `logs-*` data view
3. Filter: `service.name: orders-api`

---

## Publishing to NuGet

### First-time setup

1. Create account at [nuget.org](https://www.nuget.org)
2. Generate an API key with **Push** permissions
3. Add it as a GitHub secret: `Settings → Secrets → NUGET_API_KEY`

### Release a new version

```bash
# Tag the commit — CI will pack and publish automatically
git tag v1.2.0
git push origin v1.2.0
```

The GitHub Actions workflow will:
1. Build and run all tests
2. Pack `.nupkg` + `.snupkg` (symbols) with the tag version
3. Push to `nuget.org`

### Manual pack (for testing)

```bash
cd Company.Logging

dotnet pack src/Company.Logging.Abstractions \
  --configuration Release \
  /p:Version=1.0.0-local \
  --output ./artifacts

dotnet pack src/Company.Logging.Serilog.AspNetCore \
  --configuration Release \
  /p:Version=1.0.0-local \
  --output ./artifacts
```

### Use a private NuGet feed (GitHub Packages / Azure Artifacts)

Change the publish step in `ci.yml`:

```yaml
- name: Push to GitHub Packages
  run: |
    dotnet nuget push ./artifacts/nuget/*.nupkg \
      --api-key ${{ secrets.GITHUB_TOKEN }} \
      --source https://nuget.pkg.github.com/yourcompany/index.json
```

---

## FAQ / Troubleshooting

**Q: Logs appear in console but not Elasticsearch**  
A: Check `SelfLog` output. Add to `Program.cs`:
```csharp
Serilog.Debugging.SelfLog.Enable(Console.Error);
```

**Q: How do I change the minimum log level?**  
A: Use the standard `Serilog` section (supported via `ReadFrom.Configuration`):
```json
"Serilog": {
  "MinimumLevel": { "Default": "Debug" }
}
```

**Q: My ES index has mapping conflicts**  
A: You're logging arbitrary objects. Use `LogData.WithTag()` / `LogData.WithMeta()` instead of `{@myObject}`.

**Q: How do I add custom enrichment?**  
A: Implement `ILogEventEnricher` and register it:
```csharp
services.AddSingleton<ILogEventEnricher, MyCustomEnricher>();
// Then in UseCompanySerilog callback:
builder.Host.UseCompanySerilog(config, opts => { }, (context, services, loggerConfig) =>
{
    loggerConfig.Enrich.With(services.GetRequiredService<MyCustomEnricher>());
});
```

**Q: How do I use this in a background service (no HTTP context)?**  
A: `ILogger<T>` works everywhere. The `TraceEnricher` and `CorrelationEnricher` gracefully skip when no `Activity` or `HttpContext` is present.

---

## Monitoring & Alerting

### How `AddCompanyTelemetry()` Works

`AddCompanyTelemetry()` registers three independent subsystems in a single call:

| Subsystem | What it does |
|---|---|
| **Tracing** | Wires `OpenTelemetry.Trace` with ASP.NET Core + HttpClient instrumentation and a default `Company.{ServiceName}` `ActivitySource`. |
| **Metrics** | Wires `OpenTelemetry.Metrics` with RED metrics (`requests.total`, `requests.duration`, `requests.active`, `errors.total`) and runtime/ASP.NET Core meters. |
| **Elastic APM** | Optionally activates the Elastic APM .NET agent (`Elastic.Apm.NetCoreAll`) for auto-instrumentation. |

```csharp
// Program.cs
builder.Services.AddCompanyLogging(builder.Configuration);
builder.Services.AddCompanyTelemetry(builder.Configuration);   // ← add this

var app = builder.Build();
app.UseCompanyLogging();
app.UseCompanyTelemetry();                                       // ← and this
```

### TelemetryOptions Reference

| Key | Type | Default | Description |
|---|---|---|---|
| `Telemetry.Enabled` | bool | `true` | Master switch. `false` disables all telemetry. |
| `Telemetry.ServiceName` | string | `""` | Overrides resource `service.name`. Falls back to `CompanyLogging.ServiceName`. |
| `Telemetry.ServiceVersion` | string | `""` | Overrides resource `service.version`. |
| `Telemetry.Otlp.Enabled` | bool | `false` | Enable OTLP exporter for traces + metrics. |
| `Telemetry.Otlp.Endpoint` | string | `http://localhost:4317` | OTLP collector gRPC endpoint. |
| `Telemetry.Otlp.Protocol` | string | `grpc` | `grpc` or `http/protobuf`. |
| `Telemetry.ElasticApm.Enabled` | bool | `false` | Activate Elastic APM agent. |
| `Telemetry.ElasticApm.ServerUrl` | string | `http://localhost:8200` | APM Server URL. |
| `Telemetry.ElasticApm.ApiKey` | string | `""` | Elastic Cloud API key. |
| `Telemetry.ElasticApm.SecretToken` | string | `""` | APM Server secret token. |
| `Telemetry.Tracing.Enabled` | bool | `true` | Enable tracing pipeline. |
| `Telemetry.Tracing.SampleRatio` | double | `1.0` | Trace sample rate. `1.0` = 100%, `0.1` = 10%. |
| `Telemetry.Tracing.InstrumentAspNetCore` | bool | `true` | Auto-instrument incoming HTTP requests. |
| `Telemetry.Tracing.InstrumentHttpClient` | bool | `true` | Auto-instrument `HttpClient`. |
| `Telemetry.Tracing.InstrumentEntityFramework` | bool | `false` | Auto-instrument EF Core (requires package). |
| `Telemetry.Tracing.AdditionalSources` | string[] | `[]` | Extra `ActivitySource` names. |
| `Telemetry.Metrics.Enabled` | bool | `true` | Enable metrics pipeline. |
| `Telemetry.Metrics.InstrumentAspNetCore` | bool | `true` | ASP.NET Core request metrics. |
| `Telemetry.Metrics.InstrumentHttpClient` | bool | `true` | HttpClient metrics. |
| `Telemetry.Metrics.InstrumentRuntime` | bool | `true` | .NET runtime metrics (GC, heap, threadpool). |
| `Telemetry.Metrics.AdditionalMeters` | string[] | `[]` | Extra `Meter` names. |

### Local Dev: View Traces in Jaeger

Start the stack:
```bash
docker compose up -d
```

Open **http://localhost:16686** → select `orders-api` → search traces.

To send traces from your local dev machine, set in `appsettings.Development.json`:
```json
{
  "CompanyLogging": {
    "Telemetry": {
      "Otlp": {
        "Enabled": true,
        "Endpoint": "http://localhost:4317"
      }
    }
  }
}
```

### Production: OTLP Backend or Elastic APM

**OTLP (any vendor — Grafana, Datadog, Honeycomb, etc.):**
```json
{
  "CompanyLogging": {
    "Telemetry": {
      "Otlp": {
        "Enabled": true,
        "Endpoint": "https://otel-collector.example.com:4317",
        "Protocol": "grpc"
      }
    }
  }
}
```

**Elastic APM:**
```json
{
  "CompanyLogging": {
    "Telemetry": {
      "ElasticApm": {
        "Enabled": true,
        "ServerUrl": "https://apm.example.com:8200",
        "SecretToken": "<secret>"
      }
    }
  }
}
```

### Creating a Custom Trace Span

```csharp
app.MapPost("/orders", async (
    IActivitySourceFactory asf,
    ILogger<Program> logger,
    ...) =>
{
    // Child of the incoming HTTP span — linked to log trace.id automatically
    using var span = asf.GetSource().StartActivity("ProcessOrder");
    span?.SetTag("order.id",         orderId);
    span?.SetTag("order.item_count", items.Count);

    // Logs emitted here will carry the same trace.id / span.id
    logger.LogInformation("Processing order {OrderId}", orderId);

    span?.SetTag("order.status", "created");
});
```

### How `trace.id` Links Logs ↔ Traces ↔ Metrics

```
┌─────────────────────────────────────────────────────────┐
│  Incoming HTTP Request                                  │
│  X-Correlation-Id: abc-123  →  CorrelationMiddleware   │
│                                                         │
│  OTEL SDK creates root span ──────────┐                 │
│     trace.id = abc...def              │ W3C TraceContext │
│     span.id  = 1234abcd               │                 │
│                                       ↓                 │
│  Serilog TraceEnricher picks up Activity.Current        │
│     log fields: trace.id, span.id    ← linked here     │
│                                                         │
│  Custom spans via IActivitySourceFactory                │
│     → child spans share same trace.id                  │
│                                                         │
│  CompanyMetricsMiddleware records:                      │
│     company.http.requests.total{route="/orders"}        │
│     company.http.requests.duration{route="/orders"}     │
│     (correlated by service.name + time window)          │
└─────────────────────────────────────────────────────────┘
```

Search Kibana **Logs**: `trace.id: abc...def` → see all log lines for the request.  
Search Jaeger **Traces**: trace ID `abc...def` → see the full span waterfall.  
Search Kibana **Metrics**: `service.name: orders-api` → see RED dashboard.

### Kibana Alerting Setup

**Error rate > 5%** (Kibana → Stack Management → Rules):
```
Count where log.level: "error"  /  Count of all docs  > 0.05
  window: 5 minutes
  action: notify Slack / email
```

**P99 latency > 2 s** (Kibana → Observability → APM → Services):
```
Latency threshold: p99 > 2000 ms
  window: 5 minutes
  environments: production
```

---

## License

MIT © YourCompany
