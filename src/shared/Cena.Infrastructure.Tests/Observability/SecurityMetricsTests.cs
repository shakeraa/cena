// =============================================================================
// FIND-sec-014: Security Metrics Tests
//
// Verifies that all 6 security instruments emit correctly and that
// the metrics integrate properly with the DI container.
// =============================================================================

using System.Diagnostics.Metrics;
using Cena.Infrastructure.Observability;
using Microsoft.Extensions.DependencyInjection;

namespace Cena.Infrastructure.Tests.Observability;

public class SecurityMetricsTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly SecurityMetrics _metrics;
    private readonly MeterListener _listener;
    private readonly List<Measurement<long>> _counterMeasurements;
    private readonly List<Measurement<double>> _histogramMeasurements;

    public SecurityMetricsTests()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IMeterFactory, TestMeterFactory>();
        services.AddSecurityMetrics();
        
        _serviceProvider = services.BuildServiceProvider();
        _metrics = _serviceProvider.GetRequiredService<SecurityMetrics>();
        
        _counterMeasurements = new List<Measurement<long>>();
        _histogramMeasurements = new List<Measurement<double>>();
        
        _listener = new MeterListener();
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == "cena.security")
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        _listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
        {
            _counterMeasurements.Add(new Measurement<long>(measurement, tags));
        });
        _listener.SetMeasurementEventCallback<double>((instrument, measurement, tags, state) =>
        {
            _histogramMeasurements.Add(new Measurement<double>(measurement, tags));
        });
        _listener.Start();
    }

    public void Dispose()
    {
        _listener.Dispose();
        _metrics.Dispose();
        _serviceProvider.Dispose();
    }

    [Fact]
    public void TenantRejection_EmitsMetric()
    {
        // Act
        _metrics.RecordTenantRejection("test-service", "/api/test", "student");

        // Assert
        var measurement = _counterMeasurements.FirstOrDefault(m => 
            m.Tags.ToArray().Any(t => t.Key == "service" && (string?)t.Value == "test-service"));
        Assert.NotNull(measurement);
        Assert.Equal(1, measurement.Value);
    }

    [Fact]
    public void AuthRejection_EmitsMetric()
    {
        // Act
        _metrics.RecordAuthRejection("student-api", "token_expired");

        // Assert
        var measurement = _counterMeasurements.FirstOrDefault(m =>
            m.Tags.ToArray().Any(t => t.Key == "reason" && (string?)t.Value == "token_expired"));
        Assert.NotNull(measurement);
        Assert.Equal(1, measurement.Value);
    }

    [Fact]
    public void RateLimitRejection_EmitsMetric()
    {
        // Act
        _metrics.RecordRateLimitRejection("api_rate_limit");

        // Assert
        var measurement = _counterMeasurements.FirstOrDefault(m =>
            m.Tags.ToArray().Any(t => t.Key == "policy_name" && (string?)t.Value == "api_rate_limit"));
        Assert.NotNull(measurement);
        Assert.Equal(1, measurement.Value);
    }

    [Fact]
    public void PrivilegedAction_EmitsMetric()
    {
        // Act
        _metrics.RecordPrivilegedAction("assign_role", "admin-001");

        // Assert
        var measurement = _counterMeasurements.FirstOrDefault(m =>
            m.Tags.ToArray().Any(t => t.Key == "action" && (string?)t.Value == "assign_role"));
        Assert.NotNull(measurement);
        Assert.Equal(1, measurement.Value);
    }

    [Fact]
    public void SignalrConnectRejection_EmitsMetric()
    {
        // Act
        _metrics.RecordSignalrConnectRejection("actors-host", "auth_failed");

        // Assert
        var measurement = _counterMeasurements.FirstOrDefault(m =>
            m.Tags.ToArray().Any(t => t.Key == "service" && (string?)t.Value == "actors-host"));
        Assert.NotNull(measurement);
        Assert.Equal(1, measurement.Value);
    }

    [Fact]
    public void FirebaseTokenValidationDuration_EmitsMetric()
    {
        // Act
        _metrics.RecordFirebaseTokenValidationDuration(150.5, "student-api");

        // Assert
        var measurement = _histogramMeasurements.FirstOrDefault(m =>
            m.Tags.ToArray().Any(t => t.Key == "service" && (string?)t.Value == "student-api"));
        Assert.NotNull(measurement);
        Assert.Equal(150.5, measurement.Value);
    }

    [Fact]
    public void AllPrivilegedActions_AreRecordable()
    {
        // Arrange
        var actions = new[] { "assign_role", "gdpr_export", "gdpr_erasure", 
            "suspend_user", "force_reset", "revoke_session" };

        // Act
        foreach (var action in actions)
        {
            _metrics.RecordPrivilegedAction(action, $"admin-{action}");
        }

        // Assert
        foreach (var action in actions)
        {
            var measurement = _counterMeasurements.FirstOrDefault(m =>
                m.Tags.ToArray().Any(t => t.Key == "action" && (string?)t.Value == action));
            Assert.NotNull(measurement);
            Assert.Equal(1, measurement.Value);
        }
    }

    [Fact]
    public void MultipleMeasurements_Accumulate()
    {
        // Act
        _metrics.RecordTenantRejection("svc1", "/api/a", "student");
        _metrics.RecordTenantRejection("svc1", "/api/b", "student");
        _metrics.RecordTenantRejection("svc2", "/api/a", "student");

        // Assert
        var measurements = _counterMeasurements.Where(m =>
            m.Tags.ToArray().Any(t => t.Key == "service" && (string?)t.Value == "svc1")).ToList();
        Assert.Equal(2, measurements.Count);
    }

    /// <summary>
    /// Test helper MeterFactory that creates isolated meters for testing.
    /// </summary>
    private class TestMeterFactory : IMeterFactory
    {
        private readonly List<Meter> _meters = new();

        public Meter Create(MeterOptions options)
        {
            var meter = new Meter(options);
            _meters.Add(meter);
            return meter;
        }

        public void Dispose()
        {
            foreach (var meter in _meters)
            {
                meter.Dispose();
            }
        }
    }
}
