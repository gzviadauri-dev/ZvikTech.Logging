using Company.Logging.Telemetry.AspNetCore.Configuration;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Company.Logging.Tests;

/// <summary>
/// Verifies that the full "CompanyLogging:Telemetry" section binds correctly
/// from JSON configuration, including all nested sub-sections.
/// </summary>
public sealed class TelemetryOptionsBindingTests
{
    private static IOptions<TelemetryOptions> BuildOptions(string json)
    {
        var config = new ConfigurationBuilder()
            .AddJsonStream(new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes(json)))
            .Build();

        var services = new ServiceCollection();
        services
            .AddOptions<TelemetryOptions>()
            .Bind(config.GetSection(TelemetryOptions.SectionName));

        return services.BuildServiceProvider()
                       .GetRequiredService<IOptions<TelemetryOptions>>();
    }

    [Fact]
    public void Defaults_AreCorrect_WhenSectionIsEmpty()
    {
        var opts = BuildOptions("""{ "CompanyLogging": { "Telemetry": {} } }""").Value;

        opts.Enabled.Should().BeTrue();
        opts.Otlp.Enabled.Should().BeFalse();
        opts.Otlp.Protocol.Should().Be("grpc");
        opts.ElasticApm.Enabled.Should().BeFalse();
        opts.Tracing.Enabled.Should().BeTrue();
        opts.Tracing.SampleRatio.Should().Be(1.0);
        opts.Tracing.InstrumentAspNetCore.Should().BeTrue();
        opts.Tracing.InstrumentHttpClient.Should().BeTrue();
        opts.Metrics.Enabled.Should().BeTrue();
        opts.Metrics.InstrumentRuntime.Should().BeTrue();
    }

    [Fact]
    public void FullSection_BindsAllFields()
    {
        const string json = """
        {
          "CompanyLogging": {
            "Telemetry": {
              "Enabled": true,
              "ServiceName": "orders-api",
              "ServiceVersion": "2.5.0",
              "Otlp": {
                "Enabled": true,
                "Endpoint": "http://otel-collector:4317",
                "Protocol": "http/protobuf"
              },
              "ElasticApm": {
                "Enabled": true,
                "ServerUrl": "http://apm-server:8200",
                "SecretToken": "s3cr3t"
              },
              "Tracing": {
                "Enabled": true,
                "SampleRatio": 0.25,
                "InstrumentAspNetCore": false,
                "InstrumentHttpClient": true,
                "AdditionalSources": ["MyApp.Orders", "MyApp.Payments"]
              },
              "Metrics": {
                "Enabled": true,
                "InstrumentRuntime": false,
                "AdditionalMeters": ["MyApp.CustomMetrics"]
              }
            }
          }
        }
        """;

        var opts = BuildOptions(json).Value;

        opts.ServiceName.Should().Be("orders-api");
        opts.ServiceVersion.Should().Be("2.5.0");

        opts.Otlp.Enabled.Should().BeTrue();
        opts.Otlp.Endpoint.Should().Be("http://otel-collector:4317");
        opts.Otlp.Protocol.Should().Be("http/protobuf");

        opts.ElasticApm.Enabled.Should().BeTrue();
        opts.ElasticApm.ServerUrl.Should().Be("http://apm-server:8200");
        opts.ElasticApm.SecretToken.Should().Be("s3cr3t");

        opts.Tracing.SampleRatio.Should().Be(0.25);
        opts.Tracing.InstrumentAspNetCore.Should().BeFalse();
        opts.Tracing.AdditionalSources.Should().BeEquivalentTo(["MyApp.Orders", "MyApp.Payments"]);

        opts.Metrics.InstrumentRuntime.Should().BeFalse();
        opts.Metrics.AdditionalMeters.Should().ContainSingle("MyApp.CustomMetrics");
    }

    [Fact]
    public void MasterSwitch_DisablesEverything_WhenFalse()
    {
        const string json = """
        { "CompanyLogging": { "Telemetry": { "Enabled": false } } }
        """;

        var opts = BuildOptions(json).Value;
        opts.Enabled.Should().BeFalse();
    }
}
