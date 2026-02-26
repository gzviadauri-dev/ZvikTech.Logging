using System.Diagnostics;
using System.Diagnostics.Metrics;
using Company.Logging.Abstractions;
using Company.Logging.Telemetry.AspNetCore.Configuration;
using Company.Logging.Telemetry.AspNetCore.Instrumentation;
using Company.Logging.Telemetry.AspNetCore.Metrics;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Company.Logging.Tests;

/// <summary>
/// Verifies that <see cref="CompanyMetricsCollector"/> increments counters correctly.
/// Uses <see cref="MeterListener"/> to intercept metric measurements without a
/// full OpenTelemetry pipeline.
/// </summary>
public sealed class CompanyMetricsCollectorTests : IDisposable
{
    private readonly IMeterAccessor  _meterAccessor;
    private readonly MeterListener   _listener;
    private readonly List<(string Instrument, long Value, TagList Tags)> _longMeasurements  = new();
    private readonly List<(string Instrument, double Value, TagList Tags)> _doubleMeasurements = new();

    public CompanyMetricsCollectorTests()
    {
        var opts = Options.Create(new TelemetryOptions
        {
            ServiceName    = "test-service",
            ServiceVersion = "1.0.0"
        });
        _meterAccessor = new DefaultMeterAccessor(opts);

        _listener = new MeterListener();
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name.StartsWith("Company."))
                listener.EnableMeasurementEvents(instrument);
        };
        _listener.SetMeasurementEventCallback<long>((inst, value, tags, _) =>
            _longMeasurements.Add((inst.Name, value, new TagList(tags))));
        _listener.SetMeasurementEventCallback<double>((inst, value, tags, _) =>
            _doubleMeasurements.Add((inst.Name, value, new TagList(tags))));
        _listener.Start();
    }

    [Fact]
    public void RecordRequest_IncrementsTotal_AndRecordsDuration()
    {
        var collector = new CompanyMetricsCollector(_meterAccessor);

        collector.RecordRequest("GET", "/orders", 200, 42.5);
        _listener.RecordObservableInstruments();

        _longMeasurements.Should().ContainSingle(m => m.Instrument == "company.http.requests.total");
        _doubleMeasurements.Should().ContainSingle(m =>
            m.Instrument == "company.http.requests.duration" &&
            Math.Abs(m.Value - 42.5) < 0.001);
    }

    [Fact]
    public void RecordRequest_Tags_ContainMethodRouteAndStatus()
    {
        var collector = new CompanyMetricsCollector(_meterAccessor);

        collector.RecordRequest("POST", "/orders", 201, 10.0);
        _listener.RecordObservableInstruments();

        var total = _longMeasurements.Should().ContainSingle(m =>
            m.Instrument == "company.http.requests.total").Subject;

        ContainsTag(total.Tags, "http.method",      "POST").Should().BeTrue();
        ContainsTag(total.Tags, "http.route",       "/orders").Should().BeTrue();
        ContainsTag(total.Tags, "http.status_code", 201).Should().BeTrue();
    }

    [Fact]
    public void RecordError_IncrementsErrorCounter_WithExceptionTypeTag()
    {
        var collector = new CompanyMetricsCollector(_meterAccessor);
        var ex        = new InvalidOperationException("boom");

        collector.RecordError(ex, "GET", "/orders/123");
        _listener.RecordObservableInstruments();

        var error = _longMeasurements.Should().ContainSingle(m =>
            m.Instrument == "company.errors.total").Subject;

        ContainsTag(error.Tags, "exception.type", "InvalidOperationException").Should().BeTrue();
        ContainsTag(error.Tags, "http.method",    "GET").Should().BeTrue();
    }

    [Fact]
    public void ActiveCounter_IncrementsThenDecrements()
    {
        var collector = new CompanyMetricsCollector(_meterAccessor);

        collector.HttpRequestsActive.Add(1);
        collector.HttpRequestsActive.Add(-1);
        _listener.RecordObservableInstruments();

        var changes = _longMeasurements
            .Where(m => m.Instrument == "company.http.requests.active")
            .Select(m => m.Value)
            .ToList();

        changes.Should().Contain(1);
        changes.Should().Contain(-1);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static bool ContainsTag<T>(TagList tags, string key, T expected)
    {
        foreach (var kvp in tags)
        {
            if (kvp.Key == key && kvp.Value is T val && val!.Equals(expected))
                return true;
        }
        return false;
    }

    public void Dispose()
    {
        _listener.Dispose();
        (_meterAccessor as IDisposable)?.Dispose();
    }
}
