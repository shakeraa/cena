// =============================================================================
// Cena Platform — CAS Binding Startup Check (RDY-036)
//
// On Admin Host startup, probe the CAS engine stack with a canonical
// expression. In Enforce mode this is a hard gate — if the probe fails
// after 3 retries the host refuses to serve traffic. In Shadow/Off it's
// a warning-only diagnostic.
//
// Implements IHostedService so it runs once at boot, blocks completion of
// startup, and surfaces failures via the application's standard logger +
// the cena_cas_startup_ok gauge.
// =============================================================================

using System.Diagnostics.Metrics;
using Cena.Actors.Cas;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Cena.Admin.Api.Startup;

public sealed class CasBindingStartupCheck : IHostedService
{
    private static readonly Meter Meter = new("Cena.Cas.Gate", "1.0");
    private static int _lastResult; // 1 = ok, 0 = fail
    private static readonly ObservableGauge<int> StartupOkGauge = Meter.CreateObservableGauge(
        "cena_cas_startup_ok",
        () => new Measurement<int>(_lastResult),
        description: "1 if CAS engine probe succeeded at startup, 0 otherwise");

    public const int MaxRetries = 3;
    public const int RetryDelayMs = 1000;
    public const string ProbeExpression = "x + 1";

    private readonly ICasRouterService _router;
    private readonly ICasGateModeProvider _gateMode;
    private readonly ILogger<CasBindingStartupCheck> _logger;
    private readonly IHostApplicationLifetime _lifetime;

    public CasBindingStartupCheck(
        ICasRouterService router,
        ICasGateModeProvider gateMode,
        ILogger<CasBindingStartupCheck> logger,
        IHostApplicationLifetime lifetime)
    {
        _router = router;
        _gateMode = gateMode;
        _logger = logger;
        _lifetime = lifetime;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        // Run on a Task so we don't block host startup synchronously — the
        // probe itself is async but the lifetime hook below wires the result
        // before the application starts serving traffic.
        for (int attempt = 1; attempt <= MaxRetries; attempt++)
        {
            try
            {
                var req = new CasVerifyRequest(
                    Operation: CasOperation.NormalForm,
                    ExpressionA: ProbeExpression,
                    ExpressionB: null,
                    Variable: "x");

                var result = await _router.VerifyAsync(req, ct);
                if (result.Status == CasVerifyStatus.Ok)
                {
                    _lastResult = 1;
                    _logger.LogInformation(
                        "[CAS_STARTUP_OK] engine={Engine} latencyMs={Latency} attempt={Attempt}",
                        result.Engine, result.LatencyMs, attempt);
                    return;
                }

                _logger.LogWarning(
                    "[CAS_STARTUP_RETRY] attempt={Attempt}/{Max} status={Status} reason={Reason}",
                    attempt, MaxRetries, result.Status, result.ErrorMessage);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "[CAS_STARTUP_RETRY_EX] attempt={Attempt}/{Max}", attempt, MaxRetries);
            }

            if (attempt < MaxRetries)
                await Task.Delay(RetryDelayMs, ct);
        }

        _lastResult = 0;
        if (_gateMode.CurrentMode == CasGateMode.Enforce)
        {
            _logger.LogCritical(
                "[CAS_STARTUP_FAIL] CAS engine probe failed in Enforce mode after {Max} attempts — refusing to serve traffic",
                MaxRetries);
            // Refuse to start serving traffic in Enforce mode.
            _lifetime.StopApplication();
        }
        else
        {
            _logger.LogWarning(
                "[CAS_STARTUP_FAIL_DEGRADED] CAS engine probe failed but mode={Mode}; starting in degraded state",
                _gateMode.CurrentMode);
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
