using Company.Logging.Serilog.AspNetCore.Configuration;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Company.Logging.Tests;

public sealed class ConfigurationBindingTests
{
    [Fact]
    public void CanBind_FullCompanyLoggingSection_FromJson()
    {
        // Arrange
        var json = """
        {
          "CompanyLogging": {
            "ServiceName": "orders-api",
            "ServiceVersion": "2.1.0",
            "Environment": "prod",
            "Region": "eu-west-1",
            "CorrelationHeader": "X-Request-Id",
            "EnableRequestLogging": true,
            "EnableBodyLogging": false,
            "BodySizeLimitBytes": 2048,
            "Redaction": {
              "SensitiveKeys": ["password", "token"],
              "RedactedValue": "[HIDDEN]"
            },
            "Sampling": {
              "SampleSuccessRequests": true,
              "SuccessSampleRate": 5
            },
            "Elasticsearch": {
              "Enabled": true,
              "Uri": "http://es:9200",
              "AutoRegisterTemplate": false,
              "BatchPostingLimit": 500,
              "PeriodSeconds": 10
            },
            "Async": {
              "Enabled": true,
              "BufferSize": 5000,
              "BlockWhenFull": true
            }
          }
        }
        """;

        var config = new ConfigurationBuilder()
            .AddJsonStream(new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes(json)))
            .Build();

        var services = new ServiceCollection();
        services.AddOptions<CompanyLoggingOptions>()
            .Bind(config.GetSection(CompanyLoggingOptions.SectionName));

        var sp = services.BuildServiceProvider();
        var options = sp.GetRequiredService<IOptions<CompanyLoggingOptions>>().Value;

        // Assert all values bound correctly
        options.ServiceName.Should().Be("orders-api");
        options.ServiceVersion.Should().Be("2.1.0");
        options.Environment.Should().Be("prod");
        options.Region.Should().Be("eu-west-1");
        options.CorrelationHeader.Should().Be("X-Request-Id");
        options.EnableRequestLogging.Should().BeTrue();
        options.EnableBodyLogging.Should().BeFalse();
        options.BodySizeLimitBytes.Should().Be(2048);

        options.Redaction.SensitiveKeys.Should().Contain("password").And.Contain("token");
        options.Redaction.RedactedValue.Should().Be("[HIDDEN]");

        options.Sampling.SampleSuccessRequests.Should().BeTrue();
        options.Sampling.SuccessSampleRate.Should().Be(5);

        options.Elasticsearch.Enabled.Should().BeTrue();
        options.Elasticsearch.Uri.Should().Be("http://es:9200");
        options.Elasticsearch.BatchPostingLimit.Should().Be(500);
        options.Elasticsearch.PeriodSeconds.Should().Be(10);
        options.Elasticsearch.AutoRegisterTemplate.Should().BeFalse();

        options.Async.Enabled.Should().BeTrue();
        options.Async.BufferSize.Should().Be(5000);
        options.Async.BlockWhenFull.Should().BeTrue();
    }

    [Fact]
    public void DefaultValues_AreSet_WhenSectionIsEmpty()
    {
        // Arrange
        var config = new ConfigurationBuilder().Build();

        var services = new ServiceCollection();
        services.AddOptions<CompanyLoggingOptions>()
            .Bind(config.GetSection(CompanyLoggingOptions.SectionName));

        var sp = services.BuildServiceProvider();
        var options = sp.GetRequiredService<IOptions<CompanyLoggingOptions>>().Value;

        // Assert defaults
        options.ServiceName.Should().Be("unknown-service");
        options.Environment.Should().Be("dev");
        options.CorrelationHeader.Should().Be("X-Correlation-Id");
        options.EnableRequestLogging.Should().BeTrue();
        options.EnableBodyLogging.Should().BeFalse();
        options.BodySizeLimitBytes.Should().Be(4096);
        options.Elasticsearch.Enabled.Should().BeTrue();
        options.Elasticsearch.Uri.Should().Be("http://localhost:9200");
        options.Async.Enabled.Should().BeTrue();
        options.Async.BufferSize.Should().Be(10_000);
        options.Async.BlockWhenFull.Should().BeFalse();
    }
}
