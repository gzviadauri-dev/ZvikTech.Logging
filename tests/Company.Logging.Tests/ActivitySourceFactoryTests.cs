using System.Diagnostics;
using Company.Logging.Abstractions;
using Company.Logging.Telemetry.AspNetCore.Configuration;
using Company.Logging.Telemetry.AspNetCore.Instrumentation;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Company.Logging.Tests;

/// <summary>
/// Verifies <see cref="DefaultActivitySourceFactory"/> name convention and singleton caching.
/// </summary>
public sealed class ActivitySourceFactoryTests
{
    private static IActivitySourceFactory BuildFactory(string serviceName = "orders-api")
    {
        var opts = Options.Create(new TelemetryOptions { ServiceName = serviceName });
        return new DefaultActivitySourceFactory(opts);
    }

    [Fact]
    public void GetSource_Default_ReturnsCompanyPrefixedName()
    {
        using var factory = (DefaultActivitySourceFactory)BuildFactory("orders-api");

        var source = factory.GetSource();

        source.Name.Should().Be("Company.orders-api");
    }

    [Fact]
    public void GetSource_CustomName_ReturnsSourceWithThatName()
    {
        using var factory = (DefaultActivitySourceFactory)BuildFactory("orders-api");

        var source = factory.GetSource("MyApp.Payments");

        source.Name.Should().Be("MyApp.Payments");
    }

    [Fact]
    public void GetSource_CalledTwiceWithSameName_ReturnsSameInstance()
    {
        using var factory = (DefaultActivitySourceFactory)BuildFactory("orders-api");

        var first  = factory.GetSource();
        var second = factory.GetSource();

        first.Should().BeSameAs(second, "factory must cache instances (singleton behavior)");
    }

    [Fact]
    public void GetSource_NullOrEmpty_FallsBackToDefaultSource()
    {
        using var factory = (DefaultActivitySourceFactory)BuildFactory("my-svc");

        var fromNull  = factory.GetSource(null);
        var fromEmpty = factory.GetSource(string.Empty);
        var fromDef   = factory.GetSource();

        fromNull.Should().BeSameAs(fromDef);
        fromEmpty.Should().BeSameAs(fromDef);
    }

    [Fact]
    public void GetSource_ReturnedSource_IsActivitySource()
    {
        using var factory = (DefaultActivitySourceFactory)BuildFactory("orders-api");

        var source = factory.GetSource();

        source.Should().BeOfType<ActivitySource>();
    }
}
