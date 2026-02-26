using System.Diagnostics;
using Company.Logging.Serilog.AspNetCore.Configuration;
using Company.Logging.Serilog.AspNetCore.Middleware;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Xunit;

namespace Company.Logging.Tests;

// Run sequentially with CorrelationMiddlewareTests to prevent Activity.Current cross-contamination
[Collection("CorrelationMiddleware")]
/// <summary>
/// Verifies the Activity creation behaviour in <see cref="CorrelationMiddleware"/>.
/// </summary>
public sealed class CorrelationMiddlewareTraceTests : IDisposable
{
    // A listener that subscribes to all ActivitySources so StartActivity() actually
    // returns a non-null Activity (without a listener, ActivitySource returns null).
    private readonly ActivityListener _listener;
    private readonly List<Activity>   _started = new();

    public CorrelationMiddlewareTraceTests()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo       = _ => true,
            Sample               = (ref ActivityCreationOptions<ActivityContext> _) =>
                                       ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted      = a => _started.Add(a),
            ActivityStopped      = _ => { }
        };
        ActivitySource.AddActivityListener(_listener);
    }

    private static CorrelationMiddleware BuildMiddleware() =>
        new(Options.Create(new CompanyLoggingOptions
        {
            ServiceName       = "test-service",
            Environment       = "test",
            CorrelationHeader = "X-Correlation-Id"
        }));

    [Fact]
    public async Task InvokeAsync_StartsActivity_WhenNoParentExists()
    {
        Activity.Current = null;
        var mw = BuildMiddleware();
        var ctx = new DefaultHttpContext();
        ctx.Request.Method = "GET";
        ctx.Request.Path   = "/orders/123";

        int startedBefore = _started.Count;

        // Act
        await mw.InvokeAsync(ctx, _ => Task.CompletedTask);

        // Assert
        _started.Count.Should().BeGreaterThan(startedBefore);
    }

    [Fact]
    public async Task InvokeAsync_ActivityHasHttpMethodAndRouteTag()
    {
        Activity.Current = null; // guard against leak from other tests
        var mw  = BuildMiddleware();
        var ctx = new DefaultHttpContext();
        ctx.Request.Method = "POST";
        ctx.Request.Path   = "/orders";

        await mw.InvokeAsync(ctx, _ => Task.CompletedTask);

        var activity = _started.LastOrDefault(a =>
            a.Source.Name == "Company.Logging.CorrelationMiddleware");

        activity.Should().NotBeNull("middleware should start a fallback activity");
        activity!.GetTagItem("http.method").Should().Be("POST");
        activity.GetTagItem("http.route").Should().Be("/orders");
    }

    [Fact]
    public async Task InvokeAsync_SetsStatusCodeTag_AfterResponse()
    {
        Activity.Current = null; // guard against leak from previous test
        var mw  = BuildMiddleware();
        var ctx = new DefaultHttpContext();
        ctx.Request.Method  = "GET";
        ctx.Request.Path    = "/orders/abc";

        await mw.InvokeAsync(ctx, httpCtx =>
        {
            httpCtx.Response.StatusCode = 404;
            return Task.CompletedTask;
        });

        var activity = _started.LastOrDefault(a =>
            a.Source.Name == "Company.Logging.CorrelationMiddleware");

        activity!.GetTagItem("http.status_code").Should().Be(404);
    }

    [Fact]
    public async Task InvokeAsync_DoesNotStartActivity_WhenParentAlreadyExists()
    {
        // Start from a clean slate — other tests running in parallel may have left
        // a non-null Activity.Current via the static AsyncLocal.
        Activity.Current = null;

        // Arrange — simulate OTEL instrumentation having already created a span.
        // Explicitly assign Activity.Current because xUnit's async context does not
        // always propagate AsyncLocal changes made in the same method frame.
        using var source = new ActivitySource("test.parent");
        using var parent = source.StartActivity("ParentSpan", ActivityKind.Server);

        if (parent is null)
            return; // No listener picked up the source — test would be a false positive, skip.

        // Force Activity.Current so the middleware sees a non-null parent.
        // Save the previous value so we can restore it after the test.
        var previous = Activity.Current;
        Activity.Current = parent;

        try
        {
            var mw  = BuildMiddleware();
            var ctx = new DefaultHttpContext();
            ctx.Request.Method = "GET";
            ctx.Request.Path   = "/orders";

            // Act
            await mw.InvokeAsync(ctx, _ => Task.CompletedTask);

            // The middleware must NOT start its fallback span when a parent already exists
            _started.Where(a => a.Source.Name == "Company.Logging.CorrelationMiddleware")
                    .Should().BeEmpty("middleware must not double-span when a parent already exists");
        }
        finally
        {
            Activity.Current = previous; // prevent Activity.Current from leaking to other tests
        }
    }

    public void Dispose()
    {
        Activity.Current = null; // prevent Activity.Current from leaking across test classes
        _listener.Dispose();
    }
}
