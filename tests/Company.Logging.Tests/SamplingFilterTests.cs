using Company.Logging.Serilog.AspNetCore.Configuration;
using Company.Logging.Serilog.AspNetCore.Filters;
using FluentAssertions;
using Serilog;
using Serilog.Events;
using Serilog.Parsing;
using Xunit;

namespace Company.Logging.Tests;

public sealed class SamplingFilterTests
{
    private static LogEvent BuildLogEvent(LogEventLevel level = LogEventLevel.Information) =>
        new(
            DateTimeOffset.UtcNow,
            level,
            null,
            new MessageTemplate("", Array.Empty<MessageTemplateToken>()),
            Array.Empty<LogEventProperty>());

    [Fact]
    public void IsEnabled_ReturnsTrue_WhenSamplingDisabled()
    {
        // Arrange
        var filter = new SamplingFilter(new SamplingOptions { SampleSuccessRequests = false });

        // Act & Assert — all events pass through
        for (int i = 0; i < 100; i++)
        {
            filter.IsEnabled(BuildLogEvent()).Should().BeTrue();
        }
    }

    [Theory]
    [InlineData(LogEventLevel.Warning)]
    [InlineData(LogEventLevel.Error)]
    [InlineData(LogEventLevel.Fatal)]
    public void IsEnabled_AlwaysReturnsTrue_ForWarningsAndAbove(LogEventLevel level)
    {
        // Arrange — even with aggressive sampling, errors must never be dropped
        var filter = new SamplingFilter(new SamplingOptions
        {
            SampleSuccessRequests = true,
            SuccessSampleRate = 1000 // 0.1% sample rate
        });

        // Act & Assert
        for (int i = 0; i < 50; i++)
        {
            filter.IsEnabled(BuildLogEvent(level)).Should().BeTrue(
                $"{level} events must never be sampled out");
        }
    }

    [Fact]
    public void IsEnabled_PassesEvents_WhenNoRequestPathProperty()
    {
        // Arrange — sampling only applies to HTTP request events
        var filter = new SamplingFilter(new SamplingOptions
        {
            SampleSuccessRequests = true,
            SuccessSampleRate = 10
        });

        // Act & Assert — non-HTTP events always pass
        for (int i = 0; i < 20; i++)
        {
            filter.IsEnabled(BuildLogEvent(LogEventLevel.Information)).Should().BeTrue(
                "non-HTTP log events should not be sampled");
        }
    }

    [Fact]
    public void IsEnabled_IsThreadSafe()
    {
        // Arrange
        var filter = new SamplingFilter(new SamplingOptions
        {
            SampleSuccessRequests = false
        });

        var results = new bool[1000];

        // Act — concurrent calls
        Parallel.For(0, 1000, i =>
        {
            results[i] = filter.IsEnabled(BuildLogEvent());
        });

        // Assert — no exceptions, all pass (sampling disabled)
        results.Should().AllBeEquivalentTo(true);
    }
}