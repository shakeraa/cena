// =============================================================================
// Cena Platform — Pin the actor-host OpenTelemetry meter / source set (PRR-304)
//
// CenaActorsOpenTelemetryRegistration.AddCenaActorsOpenTelemetry was extracted
// from Program.cs in PRR-304. The behaviour-preserving extract is fine in
// principle — but a future edit dropping a meter (e.g. "Cena.Actors.Decay")
// would silently lose telemetry until ops noticed missing dashboards. This
// test pins the canonical set of ActivitySources and Meters so any add/remove
// must update both the registration and this test (i.e. it must be conscious).
//
// We assert the EXACT expected sets — both subsetting and supersetting fail.
// =============================================================================
//
// This is a static-source test (regex over the registration file). Spinning
// up the OTel pipeline in-process to enumerate registered meters at runtime
// is possible but introduces a hard dependency on OTel-internals and adds
// ~1s of test latency. The file-scan is faster, deterministic, and exactly
// matches the failure mode we want to catch (silent drift in the meter list).
// =============================================================================

using System.Text.RegularExpressions;

namespace Cena.Actors.Tests.Architecture;

public sealed class CenaActorsOpenTelemetryRegistrationTest
{
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repo root (CLAUDE.md) not found");
    }

    private static string RegistrationSource()
    {
        var repoRoot = FindRepoRoot();
        var path = Path.Combine(repoRoot, "src", "actors", "Cena.Actors.Host",
            "CenaActorsOpenTelemetryRegistration.cs");
        Assert.True(File.Exists(path), $"Registration file missing: {path}");
        return File.ReadAllText(path);
    }

    /// <summary>
    /// The full set of trace ActivitySources the actor host emits. If you
    /// add or remove one, update <see cref="ExpectedSources"/> AND the
    /// registration file together.
    /// </summary>
    private static readonly string[] ExpectedSources = new[]
    {
        "Cena.Actors.StudentActor",
        "Cena.Actors.LearningSessionActor",
        "Cena.Actors.StagnationDetectorActor",
        "Cena.Actors.OutreachSchedulerActor",
        "Proto.Actor",
    };

    /// <summary>
    /// The full set of metrics Meters the actor host emits. Drift here is
    /// the highest-cost regression — losing a meter silently breaks the
    /// /metrics scrape AND any Grafana dashboard pointed at it. Pin tightly.
    /// </summary>
    private static readonly string[] ExpectedMeters = new[]
    {
        "Cena.Actors.StudentActor",
        "Cena.Actors.LearningSessionActor",
        "Cena.Actors.LlmCircuitBreaker",
        "Cena.Actors.CurriculumGraph",
        "Cena.Actors.DeadLetterWatcher",
        "Cena.Infrastructure.NatsOutbox",
        "Cena.Actors.Decay",
        "Cena.Actors.Focus",
        "Cena.Actors.HealthAggregator",
        "Cena.Session.Nats",
        "Npgsql",
        "Cena.HttpCircuitBreaker",
    };

    [Fact]
    public void Activity_sources_match_expected_set_exactly()
    {
        var src = RegistrationSource();
        var found = Regex.Matches(src, @"\.AddSource\(""([^""]+)""\)")
            .Select(m => m.Groups[1].Value)
            .ToHashSet();

        var expected = ExpectedSources.ToHashSet();
        var missing = expected.Except(found).ToList();
        var extra = found.Except(expected).ToList();

        Assert.True(missing.Count == 0,
            "ActivitySources missing from CenaActorsOpenTelemetryRegistration:\n  "
            + string.Join("\n  ", missing));
        Assert.True(extra.Count == 0,
            "Unexpected ActivitySources in CenaActorsOpenTelemetryRegistration "
            + "(update ExpectedSources in this test if intentional):\n  "
            + string.Join("\n  ", extra));
    }

    [Fact]
    public void Meters_match_expected_set_exactly()
    {
        var src = RegistrationSource();
        // Match both literal-string meters and well-known-constant meters
        // (e.g. OcrMetrics.MeterName). For the latter we assert separately
        // below — here we just collect the literal-string ones.
        var found = Regex.Matches(src, @"\.AddMeter\(""([^""]+)""\)")
            .Select(m => m.Groups[1].Value)
            .ToHashSet();

        var expected = ExpectedMeters.ToHashSet();
        var missing = expected.Except(found).ToList();
        var extra = found.Except(expected).ToList();

        Assert.True(missing.Count == 0,
            "Meters missing from CenaActorsOpenTelemetryRegistration:\n  "
            + string.Join("\n  ", missing));
        Assert.True(extra.Count == 0,
            "Unexpected literal-string meters in CenaActorsOpenTelemetryRegistration "
            + "(update ExpectedMeters in this test if intentional):\n  "
            + string.Join("\n  ", extra));
    }

    [Fact]
    public void OcrMetrics_meter_constant_is_registered_alongside_literal_meters()
    {
        var src = RegistrationSource();
        // RDY-OCR-OBSERVABILITY: the OCR cascade meter is registered via the
        // typed constant, not a literal. Pin the reference so a refactor
        // can't silently drop it.
        Assert.Matches(@"\.AddMeter\([^""].*OcrMetrics\.MeterName.*\)", src);
    }

    [Fact]
    public void Auto_instrumentations_present_on_both_pipelines()
    {
        var src = RegistrationSource();

        // Trace pipeline: AspNetCore is required for incoming-request spans.
        Assert.Matches(@"WithTracing\([\s\S]*?AddAspNetCoreInstrumentation\(\)", src);

        // Metric pipeline: AspNetCore + Runtime + Process are all required.
        Assert.Matches(@"WithMetrics\([\s\S]*?AddAspNetCoreInstrumentation\(\)", src);
        Assert.Matches(@"WithMetrics\([\s\S]*?AddRuntimeInstrumentation\(\)", src);
        Assert.Matches(@"WithMetrics\([\s\S]*?AddProcessInstrumentation\(\)", src);
    }

    [Fact]
    public void Both_pipelines_export_to_otlp_endpoint_parameter()
    {
        var src = RegistrationSource();
        // OTLP exporter must consume the otlpEndpoint parameter (not a literal),
        // on BOTH pipelines.
        var otlpUses = Regex.Matches(src,
            @"\.AddOtlpExporter\(o => o\.Endpoint = new System\.Uri\(otlpEndpoint\)\)").Count;
        Assert.Equal(2, otlpUses);
    }

    [Fact]
    public void Prometheus_exporter_registered_on_metrics_pipeline()
    {
        var src = RegistrationSource();
        // The /metrics endpoint relies on AddPrometheusExporter on the
        // metrics pipeline. Drop it and the scrape silently 404s.
        Assert.Contains(".AddPrometheusExporter()", src);
    }

    [Fact]
    public void Resource_carries_serviceName_serviceVersion_and_instanceId()
    {
        var src = RegistrationSource();
        Assert.Matches(@"\.AddService\([\s\S]*?serviceName:\s*serviceName", src);
        Assert.Matches(@"\.AddService\([\s\S]*?serviceVersion:\s*serviceVersion", src);
        Assert.Matches(@"\.AddService\([\s\S]*?serviceInstanceId:\s*System\.Environment\.MachineName", src);
    }

    [Fact]
    public void Extension_method_signature_is_stable()
    {
        var src = RegistrationSource();
        // Stable contract: callers in Program.cs depend on this exact shape.
        Assert.Matches(
            @"public static IServiceCollection AddCenaActorsOpenTelemetry\(\s*"
            + @"this IServiceCollection services,\s*"
            + @"string otlpEndpoint,\s*"
            + @"string serviceName,\s*"
            + @"string serviceVersion\)",
            src);
    }
}
