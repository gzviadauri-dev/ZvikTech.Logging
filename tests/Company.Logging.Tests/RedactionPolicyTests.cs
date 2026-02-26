using Company.Logging.Serilog.AspNetCore.Configuration;
using Company.Logging.Serilog.AspNetCore.Filters;
using FluentAssertions;
using Serilog;
using Serilog.Sinks.InMemory;
using Xunit;

namespace Company.Logging.Tests;

public sealed class RedactionPolicyTests
{
    private static (ILogger logger, InMemorySink sink) BuildLogger(RedactionOptions? options = null)
    {
        var opts = options ?? new RedactionOptions();
        var sink = new InMemorySink();
        var logger = new LoggerConfiguration()
            .Enrich.With(new RedactionPolicy(opts))
            .WriteTo.Sink(sink)
            .CreateLogger();
        return (logger, sink);
    }

    [Theory]
    [InlineData("password")]
    [InlineData("Password")]
    [InlineData("PASSWORD")]
    [InlineData("secret")]
    [InlineData("token")]
    [InlineData("apikey")]
    [InlineData("api_key")]
    [InlineData("authorization")]
    [InlineData("clientsecret")]
    public void Redacts_SensitiveKeys_CaseInsensitive(string key)
    {
        // Arrange
        var (logger, sink) = BuildLogger();

        // Act — push a sensitive property via template
        logger.Information("Test {" + key + "}", "super-secret-value");

        // Assert
        var logEvent = sink.LogEvents.Should().ContainSingle().Subject;
        if (logEvent.Properties.TryGetValue(key, out var prop))
        {
            prop.ToString().Should().Be("\"***\"",
                $"property '{key}' should be redacted to '***'");
        }
    }

    [Fact]
    public void DoesNotRedact_NonSensitiveKeys()
    {
        // Arrange
        var (logger, sink) = BuildLogger();

        // Act
        logger.Information("Order {OrderId} placed by {UserId}", "order-123", "user-456");

        // Assert
        var logEvent = sink.LogEvents.Should().ContainSingle().Subject;
        logEvent.Properties["OrderId"].ToString().Should().Be("\"order-123\"");
        logEvent.Properties["UserId"].ToString().Should().Be("\"user-456\"");
    }

    [Fact]
    public void UsesCustomRedactedValue()
    {
        // Arrange
        var opts = new RedactionOptions { RedactedValue = "[REDACTED]" };
        var (logger, sink) = BuildLogger(opts);

        // Act
        logger.Information("User password: {password}", "my-password");

        // Assert
        var logEvent = sink.LogEvents.Should().ContainSingle().Subject;
        logEvent.Properties["password"].ToString().Should().Be("\"[REDACTED]\"");
    }

    [Fact]
    public void CanAddCustomSensitiveKeys()
    {
        // Arrange
        var opts = new RedactionOptions();
        opts.SensitiveKeys.Add("myCustomSecret");
        var (logger, sink) = BuildLogger(opts);

        // Act
        logger.Information("Value: {myCustomSecret}", "should-be-redacted");

        // Assert
        var logEvent = sink.LogEvents.Should().ContainSingle().Subject;
        if (logEvent.Properties.TryGetValue("myCustomSecret", out var prop))
        {
            prop.ToString().Should().Be("\"***\"");
        }
    }

    [Fact]
    public void DoesNotThrow_WhenNoSensitivePropertiesPresent()
    {
        // Arrange
        var (logger, sink) = BuildLogger();

        // Act & Assert — should not throw
        var act = () => logger.Information("Simple message with no sensitive data");
        act.Should().NotThrow();
    }
}
