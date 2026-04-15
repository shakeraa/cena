// =============================================================================
// Cena Platform — CAS Gate Mode Provider (RDY-036 §12, ADR-0002)
//
// The CAS ingestion gate can run in three modes:
//   - Off:     gate is disabled; questions ingest without CAS calls.
//   - Shadow:  gate runs, records outcomes + metrics, never blocks.
//   - Enforce: gate blocks failed verifications and rejects approvals
//              without a Verified binding. Production default.
//
// Mode is resolved from env CENA_CAS_GATE_MODE first, then
// configuration key Cas:GateMode. Default = Enforce.
// =============================================================================

using System.Diagnostics.Metrics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Cena.Admin.Api.Services;

/// <summary>RDY-036 §12: CAS gate rollout mode.</summary>
public enum CasGateMode
{
    Off,
    Shadow,
    Enforce
}

/// <summary>RDY-036 §12: Read-only provider of the active CAS gate mode.</summary>
public interface ICasGateModeProvider
{
    CasGateMode CurrentMode { get; }
}

/// <summary>
/// RDY-036 §12: Resolves the CAS gate mode from env var or config.
/// Registered as singleton; the mode is locked in at process start so
/// in-flight ingestion cannot see a partially-applied mode change.
/// </summary>
public sealed class CasGateModeProvider : ICasGateModeProvider
{
    private static readonly Meter Meter = new("Cena.Cas.Gate", "1.0");

    public CasGateMode CurrentMode { get; }

    public CasGateModeProvider(IConfiguration config, ILogger<CasGateModeProvider> logger)
    {
        var raw = Environment.GetEnvironmentVariable("CENA_CAS_GATE_MODE")
                  ?? config["Cas:GateMode"]
                  ?? "enforce";

        CurrentMode = raw.Trim().ToLowerInvariant() switch
        {
            "off" => CasGateMode.Off,
            "shadow" => CasGateMode.Shadow,
            _ => CasGateMode.Enforce
        };

        logger.LogInformation("[CAS_GATE_MODE] mode={Mode}", CurrentMode);

        // Gauge metric: 0=Off, 1=Shadow, 2=Enforce
        Meter.CreateObservableGauge(
            "cena_cas_gate_mode",
            () =>
            {
                var value = (int)CurrentMode;
                return new Measurement<int>(value,
                    new KeyValuePair<string, object?>("mode", CurrentMode.ToString().ToLowerInvariant()));
            },
            description: "Current CAS gate rollout mode (0=Off, 1=Shadow, 2=Enforce)");
    }
}
