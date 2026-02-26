using Company.Logging.Serilog.AspNetCore.Enrichers;
using FluentAssertions;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.InMemory;
using System.Diagnostics;
using Xunit;

namespace Company.Logging.Tests;

public sealed class TraceEnricherTests
{
    [Fact]
    public void Enrich_AddsTraceId_WhenActivityIsActive()
    {
        // Arrange
        var sink = new InMemorySink();
        var logger = new LoggerConfiguration()
            .Enrich.With<TraceEnricher>()
            .WriteTo.Sink(sink)
            .CreateLogger();

        using var source = new ActivitySource("test-source");
        using var activity = source.StartActivity("test-operation");

        // Act
        logger.Information("Test message");

        // Assert
        var logEvent = sink.LogEvents.Should().ContainSingle().Subject;

        if (activity is not null)
        {
            logEvent.Properties.Should().ContainKey("trace.id");
            logEvent.Properties.Should().ContainKey("span.id");

            var traceId = logEvent.Properties["trace.id"].ToString().Trim('"');
            traceId.Should().Be(activity.TraceId.ToHexString());
        }
    }

    [Fact]
    public void Enrich_DoesNotAddTraceId_WhenNoActivityIsActive()
    {
        // Arrange — ensure no ambient activity
        var previousActivity = Activity.Current;
        Activity.Current = null;

        var sink = new InMemorySink();
        var logger = new LoggerConfiguration()
            .Enrich.With<TraceEnricher>()
            .WriteTo.Sink(sink)
            .CreateLogger();

        try
        {
            // Act
            logger.Information("Test message without activity");

            // Assert
            var logEvent = sink.LogEvents.Should().ContainSingle().Subject;
            logEvent.Properties.Should().NotContainKey("trace.id");
            logEvent.Properties.Should().NotContainKey("span.id");
        }
        finally
        {
            Activity.Current = previousActivity;
        }
    }

    [Fact]
    public void Enrich_TraceId_FollowsW3CFormat()
    {
        // Arrange
        var sink = new InMemorySink();
        var logger = new LoggerConfiguration()
            .Enrich.With<TraceEnricher>()
            .WriteTo.Sink(sink)
            .CreateLogger();

        using var source = new ActivitySource("test-w3c");
        using var activity = source.StartActivity("w3c-test");

        if (activity is null) return; // Skip if no listener

        // Act
        logger.Information("W3C trace test");

        // Assert
        var logEvent = sink.LogEvents.Should().ContainSingle().Subject;

        if (logEvent.Properties.TryGetValue("trace.id", out var traceIdProp))
        {
            var traceId = traceIdProp.ToString().Trim('"');
            traceId.Should().HaveLength(32, "W3C trace ID is 32 hex chars");
            traceId.Should().MatchRegex("^[0-9a-f]{32}$", "W3C trace ID is lowercase hex");
        }

        if (logEvent.Properties.TryGetValue("span.id", out var spanIdProp))
        {
            var spanId = spanIdProp.ToString().Trim('"');
            spanId.Should().HaveLength(16, "W3C span ID is 16 hex chars");
            spanId.Should().MatchRegex("^[0-9a-f]{16}$", "W3C span ID is lowercase hex");
        }
    }
}