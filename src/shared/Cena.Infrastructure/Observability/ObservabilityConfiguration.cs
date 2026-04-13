// =============================================================================
// Cena Platform — Observability Configuration (OBS-001)
// Three-layer observability: OTel traces, structured logs, critical alerts.
// =============================================================================

using System.Diagnostics;

namespace Cena.Infrastructure.Observability;

/// <summary>
/// OBS-001: Central activity sources for OpenTelemetry tracing.
/// Each critical path gets its own source for filtering and sampling.
/// </summary>
public static class CenaActivitySources
{
    public static readonly ActivitySource CasVerification = new("Cena.CAS.Verification", "1.0.0");
    public static readonly ActivitySource StepSolving = new("Cena.StepSolver", "1.0.0");
    public static readonly ActivitySource PhotoIngestion = new("Cena.Photo.Ingestion", "1.0.0");
    public static readonly ActivitySource SessionLifecycle = new("Cena.Session.Lifecycle", "1.0.0");
    public static readonly ActivitySource TutorLlm = new("Cena.Tutor.LLM", "1.0.0");
    public static readonly ActivitySource ExamSimulation = new("Cena.Exam.Simulation", "1.0.0");
}

/// <summary>
/// OBS-001: Critical alert definitions. Each alert has a name, threshold,
/// and severity. Alerting infrastructure (Grafana/PagerDuty) reads these.
/// </summary>
public sealed record CriticalAlert(
    string AlertId,
    string Name,
    string Description,
    string Severity,
    string MetricName,
    string Condition,
    TimeSpan EvaluationWindow);

/// <summary>
/// The 6 critical alerts defined per improvement #49.
/// </summary>
public static class CriticalAlerts
{
    public static readonly CriticalAlert CasSidecarDown = new(
        "ALERT-CAS-001", "CAS Sidecar Down",
        "SymPy sidecar is unreachable. Step verification degraded.",
        "critical", "cena.cas.health", "health == 0",
        TimeSpan.FromMinutes(2));

    public static readonly CriticalAlert DailyCostExceeded = new(
        "ALERT-COST-001", "Daily AI Cost Exceeded",
        "LLM token spend exceeded daily budget threshold.",
        "warning", "cena.llm.daily_cost_usd", "value > threshold",
        TimeSpan.FromHours(1));

    public static readonly CriticalAlert ErrorRateHigh = new(
        "ALERT-ERR-001", "Error Rate > 5%",
        "API error rate exceeds 5% over the evaluation window.",
        "critical", "cena.api.error_rate", "rate > 0.05",
        TimeSpan.FromMinutes(5));

    public static readonly CriticalAlert QueueDepthHigh = new(
        "ALERT-QUEUE-001", "Queue Depth > 100",
        "NATS/outbox queue depth exceeds threshold. Processing backlog.",
        "warning", "cena.nats.queue_depth", "depth > 100",
        TimeSpan.FromMinutes(5));

    public static readonly CriticalAlert ExamIntegrityViolation = new(
        "ALERT-EXAM-001", "Exam Integrity Violation",
        "High-confidence anomaly detected during active exam simulation.",
        "critical", "cena.exam.integrity_violation", "count > 0",
        TimeSpan.FromMinutes(1));

    public static readonly CriticalAlert CsamDetection = new(
        "ALERT-CSAM-001", "CSAM Detection",
        "Photo ingestion pipeline flagged content requiring immediate review.",
        "critical", "cena.photo.csam_flag", "count > 0",
        TimeSpan.FromSeconds(30));

    public static IReadOnlyList<CriticalAlert> All => new[]
    {
        CasSidecarDown, DailyCostExceeded, ErrorRateHigh,
        QueueDepthHigh, ExamIntegrityViolation, CsamDetection
    };
}

/// <summary>
/// Golden signals metrics (Latency, Traffic, Errors, Saturation) per service.
/// </summary>
public sealed record GoldenSignals(
    string ServiceName,
    double P50LatencyMs,
    double P95LatencyMs,
    double P99LatencyMs,
    double RequestsPerSecond,
    double ErrorRate,
    double SaturationPercent);
