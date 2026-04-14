// =============================================================================
// Cena Platform — BKT Calibration Provider (RDY-024 Phase A)
//
// Provides Services.BktParameters from configuration instead of hardcoded
// defaults. Gated by FeatureFlags:BktCalibratedParams. When OFF (default
// during pilot), returns BktParameters.Default. When ON, returns per-subject
// calibrated parameters from config/bkt-params.json.
//
// Thread-safe: IOptionsMonitor reloads on config file change; dict lookup
// is O(1) with OrdinalIgnoreCase comparer.
// =============================================================================

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cena.Actors.Services;

/// <summary>
/// RDY-024: Provides <see cref="BktParameters"/> from configuration,
/// supporting per-subject calibration gated by a feature flag.
/// </summary>
public interface IBktCalibrationProvider
{
    /// <summary>
    /// Get BKT parameters for a concept. Phase A returns calibrated defaults;
    /// Phase B will add concept → subject resolution via curriculum graph.
    /// </summary>
    BktParameters GetParameters(string conceptId);

    /// <summary>
    /// Get BKT parameters for a named subject (e.g. "algebra").
    /// </summary>
    BktParameters GetParametersForSubject(string subject);
}

/// <summary>
/// Default implementation: returns <see cref="BktParameters.Default"/> always.
/// Used when no calibration config is loaded.
/// </summary>
public sealed class DefaultBktCalibrationProvider : IBktCalibrationProvider
{
    public BktParameters GetParameters(string conceptId) => BktParameters.Default;
    public BktParameters GetParametersForSubject(string subject) => BktParameters.Default;
}

/// <summary>
/// Config-driven provider that loads per-subject BKT parameters from
/// <c>config/bkt-params.json</c>, gated by <c>FeatureFlags:BktCalibratedParams</c>.
/// </summary>
public sealed class ConfigurableBktCalibrationProvider : IBktCalibrationProvider
{
    private readonly IOptionsMonitor<BktCalibrationOptions> _options;
    private readonly bool _calibratedParamsEnabled;

    public ConfigurableBktCalibrationProvider(
        IOptionsMonitor<BktCalibrationOptions> options,
        ILogger<ConfigurableBktCalibrationProvider> logger,
        IConfiguration configuration)
    {
        _options = options;

        // Static config flag — no actor request per BKT update (allocation-free hot path).
        _calibratedParamsEnabled = configuration.GetValue<bool>("FeatureFlags:BktCalibratedParams");

        logger.LogInformation(
            "BKT calibration provider initialized: enabled={Enabled}, subjects={Count}, source={Source}",
            _calibratedParamsEnabled,
            options.CurrentValue.Subjects.Count,
            options.CurrentValue.CalibrationSource ?? "defaults");
    }

    public BktParameters GetParameters(string conceptId)
    {
        if (!_calibratedParamsEnabled)
            return BktParameters.Default;

        // Phase A: no concept→subject mapping yet; return calibrated defaults.
        // Phase B will resolve via curriculum graph.
        return _options.CurrentValue.Defaults.ToBktParameters();
    }

    public BktParameters GetParametersForSubject(string subject)
    {
        if (!_calibratedParamsEnabled)
            return BktParameters.Default;

        var opts = _options.CurrentValue;

        if (opts.Subjects.TryGetValue(subject, out var subjectParams))
            return subjectParams.ToBktParameters();

        return opts.Defaults.ToBktParameters();
    }
}
