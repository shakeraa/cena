using Cena.Actors.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Cena.Actors.Tests.Services;

/// <summary>
/// RDY-024: Tests for ConfigurableBktCalibrationProvider — feature-flag gating,
/// per-subject resolution, and fallback to defaults.
/// </summary>
public sealed class ConfigurableBktCalibrationProviderTests
{
    // ── Feature flag OFF: always returns BktParameters.Default ──

    [Fact]
    public void GetParameters_FlagOff_ReturnsBktDefault()
    {
        var sut = CreateProvider(calibratedEnabled: false);

        var result = sut.GetParameters("concept-algebra-123");

        Assert.Equal(BktParameters.Default, result);
    }

    [Fact]
    public void GetParametersForSubject_FlagOff_ReturnsBktDefault()
    {
        var sut = CreateProvider(calibratedEnabled: false, subjects: new()
        {
            ["algebra"] = new SubjectBktParams { PLearning = 0.15, PSlip = 0.08 }
        });

        var result = sut.GetParametersForSubject("algebra");

        Assert.Equal(BktParameters.Default, result);
    }

    // ── Feature flag ON: returns config values ──

    [Fact]
    public void GetParametersForSubject_FlagOn_SubjectExists_ReturnsSubjectParams()
    {
        var sut = CreateProvider(calibratedEnabled: true, subjects: new()
        {
            ["algebra"] = new SubjectBktParams
            {
                PLearning = 0.15,
                PSlip = 0.08,
                PGuess = 0.22,
                PForget = 0.03,
                PInitial = 0.12
            }
        });

        var result = sut.GetParametersForSubject("algebra");

        Assert.Equal(0.15, result.PLearning);
        Assert.Equal(0.08, result.PSlip);
        Assert.Equal(0.22, result.PGuess);
        Assert.Equal(0.03, result.PForget);
        Assert.Equal(0.12, result.PInitial);
        Assert.Equal(MasteryConstants.ProgressionThreshold, result.ProgressionThreshold);
        Assert.Equal(MasteryConstants.PrerequisiteGateThreshold, result.PrerequisiteGateThreshold);
    }

    [Fact]
    public void GetParametersForSubject_FlagOn_SubjectMissing_ReturnsCalibratedDefaults()
    {
        var calibratedDefaults = new SubjectBktParams
        {
            PLearning = 0.12,
            PSlip = 0.06,
            PGuess = 0.18,
            PForget = 0.015,
            PInitial = 0.09
        };
        var sut = CreateProvider(calibratedEnabled: true, defaults: calibratedDefaults);

        var result = sut.GetParametersForSubject("unknown_subject");

        Assert.Equal(0.12, result.PLearning);
        Assert.Equal(0.06, result.PSlip);
        Assert.Equal(0.18, result.PGuess);
        Assert.Equal(0.015, result.PForget);
        Assert.Equal(0.09, result.PInitial);
    }

    [Fact]
    public void GetParametersForSubject_FlagOn_CaseInsensitiveSubjectLookup()
    {
        var sut = CreateProvider(calibratedEnabled: true, subjects: new()
        {
            ["Algebra"] = new SubjectBktParams { PLearning = 0.25 }
        });

        var result = sut.GetParametersForSubject("algebra");

        Assert.Equal(0.25, result.PLearning);
    }

    [Fact]
    public void GetParameters_FlagOn_ReturnsCalibratedDefaults()
    {
        var calibratedDefaults = new SubjectBktParams { PLearning = 0.14 };
        var sut = CreateProvider(calibratedEnabled: true, defaults: calibratedDefaults);

        var result = sut.GetParameters("any-concept-id");

        Assert.Equal(0.14, result.PLearning);
    }

    // ── SubjectBktParams.ToBktParameters ──

    [Fact]
    public void SubjectBktParams_ToBktParameters_SetsThresholdsFromMasteryConstants()
    {
        var subject = new SubjectBktParams
        {
            PLearning = 0.11,
            PSlip = 0.04,
            PGuess = 0.19,
            PForget = 0.025,
            PInitial = 0.08
        };

        var bkt = subject.ToBktParameters();

        Assert.Equal(0.11, bkt.PLearning);
        Assert.Equal(0.04, bkt.PSlip);
        Assert.Equal(0.19, bkt.PGuess);
        Assert.Equal(0.025, bkt.PForget);
        Assert.Equal(0.08, bkt.PInitial);
        Assert.Equal(MasteryConstants.ProgressionThreshold, bkt.ProgressionThreshold);
        Assert.Equal(MasteryConstants.PrerequisiteGateThreshold, bkt.PrerequisiteGateThreshold);
    }

    // ── DefaultBktCalibrationProvider (fallback) ──

    [Fact]
    public void DefaultProvider_GetParametersForSubject_ReturnsBktDefault()
    {
        var sut = new DefaultBktCalibrationProvider();

        var result = sut.GetParametersForSubject("algebra");

        Assert.Equal(BktParameters.Default, result);
    }

    // ── BktCalibrationOptions binding ──

    [Fact]
    public void BktCalibrationOptions_DefaultValues_MatchBktParametersDefault()
    {
        var opts = new BktCalibrationOptions();
        var bkt = opts.Defaults.ToBktParameters();

        Assert.Equal(BktParameters.Default.PLearning, bkt.PLearning);
        Assert.Equal(BktParameters.Default.PSlip, bkt.PSlip);
        Assert.Equal(BktParameters.Default.PGuess, bkt.PGuess);
        Assert.Equal(BktParameters.Default.PForget, bkt.PForget);
        Assert.Equal(BktParameters.Default.PInitial, bkt.PInitial);
    }

    // ── Helpers ──

    private static ConfigurableBktCalibrationProvider CreateProvider(
        bool calibratedEnabled,
        SubjectBktParams? defaults = null,
        Dictionary<string, SubjectBktParams>? subjects = null)
    {
        // Ensure OrdinalIgnoreCase matching (same as BktCalibrationOptions default)
        var normalizedSubjects = new Dictionary<string, SubjectBktParams>(StringComparer.OrdinalIgnoreCase);
        if (subjects != null)
            foreach (var kvp in subjects)
                normalizedSubjects[kvp.Key] = kvp.Value;

        var options = new BktCalibrationOptions
        {
            Version = 1,
            CalibrationSource = "test",
            Defaults = defaults ?? new SubjectBktParams(),
            Subjects = normalizedSubjects
        };

        var optionsMonitor = new TestOptionsMonitor<BktCalibrationOptions>(options);

        var configData = new Dictionary<string, string?>
        {
            ["FeatureFlags:BktCalibratedParams"] = calibratedEnabled.ToString()
        };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();

        return new ConfigurableBktCalibrationProvider(
            optionsMonitor,
            NullLogger<ConfigurableBktCalibrationProvider>.Instance,
            configuration);
    }

    private sealed class TestOptionsMonitor<T> : IOptionsMonitor<T>
    {
        public TestOptionsMonitor(T value) => CurrentValue = value;
        public T CurrentValue { get; }
        public T Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
