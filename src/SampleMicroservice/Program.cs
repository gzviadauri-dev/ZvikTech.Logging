using Company.Logging.Abstractions;
using Company.Logging.Serilog.AspNetCore.Extensions;
using Company.Logging.Telemetry.AspNetCore.Extensions;
using Microsoft.AspNetCore.Mvc;
using Serilog;

// ─────────────────────────────────────────────────────────────────────────────
// Bootstrap: configure Serilog BEFORE building the host so startup errors
// are captured with full structured context.
// ─────────────────────────────────────────────────────────────────────────────
var builder = WebApplication.CreateBuilder(args);

// 1. Wire up Company logging (reads "CompanyLogging" section from appsettings)
builder.Host.UseCompanySerilog(builder.Configuration);
builder.Services.AddCompanyLogging(builder.Configuration);

// 2. Wire up OpenTelemetry traces + metrics (reads "CompanyLogging:Telemetry")
builder.Services.AddCompanyTelemetry(builder.Configuration);

// 3. Standard services
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHealthChecks();

// ─────────────────────────────────────────────────────────────────────────────
// Build and configure middleware pipeline
// ─────────────────────────────────────────────────────────────────────────────
var app = builder.Build();

// 4. Company logging middleware (correlation + request logging) — must be early
app.UseCompanyLogging();

// 5. Company telemetry middleware (RED metrics collector)
app.UseCompanyTelemetry();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapHealthChecks("/health");

// ─────────────────────────────────────────────────────────────────────────────
// Endpoints
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Creates a fake order and logs structured data.</summary>
app.MapPost("/orders", async (
    [FromBody] CreateOrderRequest request,
    ILogger<Program> logger,
    ILogDataScope logScope,
    ICorrelationContext correlation,
    IActivitySourceFactory activitySourceFactory,
    CancellationToken ct) =>
{
    var orderId = $"order-{Guid.NewGuid():N}";

    // Create a custom trace span for the business operation.
    // This span is a child of the incoming HTTP request span (set by ASP.NET Core OTEL instrumentation).
    using var activity = activitySourceFactory.GetSource().StartActivity("ProcessOrder");
    activity?.SetTag("order.id",         orderId);
    activity?.SetTag("order.item_count", request.Items.Count);
    activity?.SetTag("order.currency",   request.Currency ?? "USD");
    activity?.SetTag("correlation.id",   correlation.CorrelationId);

    // Use LogData to push structured context without risking mapping explosion
    var logData = LogData.Create()
        .WithEvent("order.created")
        .WithEntity("Order", orderId)
        .WithTag("order.currency",   request.Currency ?? "USD")
        .WithTag("order.item_count", request.Items.Count.ToString());

    using var scope = logScope.BeginScope(logData);

    logger.LogInformation(
        "Creating order {OrderId} for customer {CustomerId} with {ItemCount} items",
        orderId, request.CustomerId, request.Items.Count);

    // Simulate async work
    await Task.Delay(50, ct);

    var order = new OrderResponse(
        Id: orderId,
        CustomerId: request.CustomerId,
        Items: request.Items,
        Status: "pending",
        CorrelationId: correlation.CorrelationId,
        CreatedAt: DateTimeOffset.UtcNow);

    activity?.SetTag("order.status", "created");
    logger.LogInformation("Order {OrderId} created successfully", orderId);

    return Results.Created($"/orders/{orderId}", order);
})
.WithName("CreateOrder")
.WithOpenApi();

/// <summary>Demonstrates exception logging with full stack trace enrichment.</summary>
app.MapGet("/orders/error-demo", (ILogger<Program> logger) =>
{
    logger.LogWarning("About to throw a demo exception");

    try
    {
        throw new InvalidOperationException("This is a demo exception to show error logging");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Demo exception caught and logged with full context");
        return Results.Problem(
            title: "Demo Error",
            detail: ex.Message,
            statusCode: 500);
    }
})
.WithName("ErrorDemo")
.WithOpenApi();

/// <summary>Retrieves a fake order — shows correlation ID propagation.</summary>
app.MapGet("/orders/{id}", (
    string id,
    ILogger<Program> logger,
    ICorrelationContext correlation,
    IActivitySourceFactory activitySourceFactory) =>
{
    using var activity = activitySourceFactory.GetSource().StartActivity("GetOrder");
    activity?.SetTag("order.id",       id);
    activity?.SetTag("correlation.id", correlation.CorrelationId);

    logger.LogInformation(
        "Retrieving order {OrderId}. CorrelationId: {CorrelationId}",
        id, correlation.CorrelationId);

    if (!id.StartsWith("order-"))
        return Results.NotFound(new { message = $"Order {id} not found" });

    var order = new OrderResponse(
        Id: id,
        CustomerId: "cust-demo",
        Items: new List<string> { "item-1", "item-2" },
        Status: "completed",
        CorrelationId: correlation.CorrelationId,
        CreatedAt: DateTimeOffset.UtcNow.AddHours(-1));

    return Results.Ok(order);
})
.WithName("GetOrder")
.WithOpenApi();

app.Run();

// ─────────────────────────────────────────────────────────────────────────────
// Models
// ─────────────────────────────────────────────────────────────────────────────
public sealed record CreateOrderRequest(
    string CustomerId,
    List<string> Items,
    string? Currency = "USD");

public sealed record OrderResponse(
    string Id,
    string CustomerId,
    List<string> Items,
    string Status,
    string CorrelationId,
    DateTimeOffset CreatedAt);
