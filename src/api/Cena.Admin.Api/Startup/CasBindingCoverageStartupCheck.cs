// =============================================================================
// Cena Platform — CAS Binding Coverage Startup Check (RDY-040, RDY-036 §5)
//
// What the OTHER CasBindingStartupCheck does: engine liveness ("can we talk
// to SymPy?"). What this service does: DATA safety — "do all Published
// math/physics questions have a Verified CAS binding?". Without this, a
// Postgres snapshot from a pre-ADR-0032 database can boot happily with
// thousands of unverified Published questions and the gate provides no
// protection.
//
// Behaviour:
//   Enforce mode + mismatch → critical log + StopApplication (refuse to
//                              serve traffic).
//   Shadow  mode + mismatch → warning log, keep running.
//   Off     mode            → skip entirely (bypass).
//   CENA_CAS_STARTUP_CHECK=skip  → skip entirely, regardless of mode, with
//                                  a critical log so ops sees it.
//
// Metric: cena_cas_binding_coverage_ratio (gauge, verified / published).
// =============================================================================

using System.Diagnostics.Metrics;
using Cena.Actors.Cas;
using Cena.Infrastructure.Documents;
using Marten;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Cena.Admin.Api.Startup;

public sealed class CasBindingCoverageStartupCheck : IHostedService
{
    public const string SkipEnvVar = "CENA_CAS_STARTUP_CHECK";

    private static readonly Meter Meter = new("Cena.Cas.Gate", "1.0");
    private static double _lastRatio = 1.0;
    private static readonly ObservableGauge<double> CoverageGauge = Meter.CreateObservableGauge(
        "cena_cas_binding_coverage_ratio",
        () => new Measurement<double>(_lastRatio),
        description: "Ratio of Verified CAS bindings to Published math/physics questions (1.0 = full coverage)");

    private static readonly string[] MathSubjects =
    {
        "math", "mathematics", "maths", "physics", "chemistry"
    };

    private readonly IDocumentStore _store;
    private readonly ICasGateModeProvider _mode;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<CasBindingCoverageStartupCheck> _logger;

    public CasBindingCoverageStartupCheck(
        IDocumentStore store,
        ICasGateModeProvider mode,
        IHostApplicationLifetime lifetime,
        ILogger<CasBindingCoverageStartupCheck> logger)
    {
        _store = store;
        _mode = mode;
        _lifetime = lifetime;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        var skipFlag = Environment.GetEnvironmentVariable(SkipEnvVar);
        if (string.Equals(skipFlag, "skip", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogCritical(
                "[CAS_COVERAGE_SKIPPED] {Env}=skip is set — startup coverage check bypassed. " +
                "This MUST only be used in dev/test; production must enforce.",
                SkipEnvVar);
            return;
        }

        if (_mode.CurrentMode == CasGateMode.Off)
        {
            _logger.LogInformation(
                "[CAS_COVERAGE_SKIPPED] gate mode=Off — coverage check not applicable");
            return;
        }

        try
        {
            await using var session = _store.QuerySession();

            // Published math/physics/chemistry questions.
            var publishedMath = await session.Query<Cena.Actors.Questions.QuestionState>()
                .Where(q =>
                    q.Status == Cena.Actors.Questions.QuestionLifecycleStatus.Published
                    && MathSubjects.Contains(q.Subject.ToLower()))
                .CountAsync(ct);

            // Verified CAS bindings.
            var verifiedBindings = await session.Query<QuestionCasBinding>()
                .Where(b => b.Status == CasBindingStatus.Verified)
                .CountAsync(ct);

            _lastRatio = publishedMath == 0 ? 1.0 : (double)verifiedBindings / publishedMath;

            _logger.LogInformation(
                "[CAS_COVERAGE] published_math={Published} verified_bindings={Verified} ratio={Ratio:P2} mode={Mode}",
                publishedMath, verifiedBindings, _lastRatio, _mode.CurrentMode);

            if (publishedMath > verifiedBindings)
            {
                var deficit = publishedMath - verifiedBindings;
                if (_mode.CurrentMode == CasGateMode.Enforce)
                {
                    _logger.LogCritical(
                        "[STARTUP_ABORT] reason=cas_binding_mismatch published={Published} verified={Verified} deficit={Deficit} " +
                        "— Admin API refuses to serve traffic in Enforce mode. Run cas-backfill or set {Env}=skip for dev/test.",
                        publishedMath, verifiedBindings, deficit, SkipEnvVar);
                    _lifetime.StopApplication();
                    return;
                }

                _logger.LogWarning(
                    "[STARTUP_WARN] reason=cas_binding_mismatch published={Published} verified={Verified} deficit={Deficit} " +
                    "— Shadow mode allows boot; flip to Enforce after running cas-backfill.",
                    publishedMath, verifiedBindings, deficit);
            }
        }
        catch (Exception ex)
        {
            // A query failure (e.g. Marten schema not yet applied) is NOT a
            // safety failure — log and continue. The engine probe remains
            // the primary "CAS is reachable" signal.
            _logger.LogWarning(ex,
                "[CAS_COVERAGE_QUERY_FAIL] could not compute binding coverage at startup — continuing. " +
                "This usually means the Marten schema is not yet applied. Re-run after migrations.");
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
