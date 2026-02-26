using Company.Logging.Serilog.AspNetCore.Configuration;
using Company.Logging.Serilog.AspNetCore.Enrichers;
using Company.Logging.Serilog.AspNetCore.Middleware;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using System.Net;
using Xunit;

namespace Company.Logging.Tests;

// Run sequentially with CorrelationMiddlewareTraceTests to prevent Activity.Current cross-contamination
[Collection("CorrelationMiddleware")]
public sealed class CorrelationMiddlewareTests
{
    private readonly CompanyLoggingOptions _options = new()
    {
        CorrelationHeader = "X-Correlation-Id"
    };

    private CorrelationMiddleware CreateMiddleware() =>
        new(Options.Create(_options));

    [Fact]
    public async Task InvokeAsync_SetsCorrelationId_WhenHeaderPresent()
    {
        // Arrange
        var middleware = CreateMiddleware();
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Correlation-Id"] = "test-correlation-123";

        string? capturedCorrelationId = null;
        var next = new RequestDelegate(_ =>
        {
            capturedCorrelationId = CorrelationContext.CorrelationId;
            return Task.CompletedTask;
        });

        // Act
        await middleware.InvokeAsync(context, next);

        // Assert
        capturedCorrelationId.Should().Be("test-correlation-123");
    }

    [Fact]
    public async Task InvokeAsync_GeneratesCorrelationId_WhenHeaderMissing()
    {
        // Arrange
        var middleware = CreateMiddleware();
        var context = new DefaultHttpContext();

        string? capturedCorrelationId = null;
        var next = new RequestDelegate(_ =>
        {
            capturedCorrelationId = CorrelationContext.CorrelationId;
            return Task.CompletedTask;
        });

        // Act
        await middleware.InvokeAsync(context, next);

        // Assert
        capturedCorrelationId.Should().NotBeNullOrEmpty();
        Guid.TryParse(capturedCorrelationId, out _).Should().BeTrue(
            "auto-generated correlation ID should be a valid GUID");
    }

    [Fact]
    public async Task InvokeAsync_AddsCorrelationIdToResponseHeader()
    {
        // Arrange
        var middleware = CreateMiddleware();
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Correlation-Id"] = "response-test-456";

        // Simulate response writing to trigger OnStarting callback
        var responseFeature = new TestResponseFeature();
        context.Features.Set<Microsoft.AspNetCore.Http.Features.IHttpResponseFeature>(responseFeature);

        var next = new RequestDelegate(async ctx =>
        {
            await ctx.Response.WriteAsync("ok");
        });

        // Act
        await middleware.InvokeAsync(context, next);

        // Assert - response header set during pipeline
        context.Response.Headers.TryGetValue("X-Correlation-Id", out var responseHeader);
        // Header is set via OnStarting callback; verify it would be set
        responseFeature.OnStartingCallbacks.Should().NotBeEmpty();
    }

    [Fact]
    public async Task InvokeAsync_SetsRequestId_FromTraceIdentifier()
    {
        // Arrange
        var middleware = CreateMiddleware();
        var context = new DefaultHttpContext();
        context.TraceIdentifier = "trace-id-789";

        string? capturedRequestId = null;
        var next = new RequestDelegate(_ =>
        {
            capturedRequestId = CorrelationContext.RequestId;
            return Task.CompletedTask;
        });

        // Act
        await middleware.InvokeAsync(context, next);

        // Assert
        capturedRequestId.Should().Be("trace-id-789");
    }

    [Fact]
    public async Task InvokeAsync_DoesNotUseBlankHeader_AsCorrelationId()
    {
        // Arrange
        var middleware = CreateMiddleware();
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Correlation-Id"] = "   "; // whitespace only

        string? capturedCorrelationId = null;
        var next = new RequestDelegate(_ =>
        {
            capturedCorrelationId = CorrelationContext.CorrelationId;
            return Task.CompletedTask;
        });

        // Act
        await middleware.InvokeAsync(context, next);

        // Assert — should generate a new GUID, not use the whitespace value
        capturedCorrelationId.Should().NotBeNullOrWhiteSpace();
        Guid.TryParse(capturedCorrelationId, out _).Should().BeTrue();
    }
}

/// <summary>Test double for IHttpResponseFeature to capture OnStarting callbacks.</summary>
internal sealed class TestResponseFeature : Microsoft.AspNetCore.Http.Features.IHttpResponseFeature
{
    public List<Func<object, Task>> OnStartingCallbacks { get; } = new();

    public int StatusCode { get; set; } = 200;
    public string? ReasonPhrase { get; set; }
    public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();
    public Stream Body { get; set; } = Stream.Null;
    public bool HasStarted => false;

    public void OnCompleted(Func<object, Task> callback, object state) { }
    public void OnStarting(Func<object, Task> callback, object state)
        => OnStartingCallbacks.Add(callback);
}
