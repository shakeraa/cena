// =============================================================================
// Cena Platform — BKT Calibration schema tests (RDY-024b prep)
//
// Two safety nets for when pilot data drives a real EM calibration run:
//
// 1. Every SubjectBktParams conversion runs validation (probabilities in
//    (0,1), PSlip+PGuess<1, PForget<PLearning). Unit-tested below so a
//    drift in validator logic is caught.
//
// 2. The checked-in config/bkt-params.json passes validation end-to-end.
//    When post-pilot calibration lands, the operator drops a new json
//    via `python scripts/bkt-calibration.py calibrate` and this test
//    catches any degenerate output BEFORE it ships to a student.
// =============================================================================

using System.Text.Json;
using Cena.Actors.Services;

namespace Cena.Actors.Tests.Services;

public class BktCalibrationValidatorTests
{
    private static SubjectBktParams Valid() => new()
    {
        PLearning = 0.10,
        PSlip = 0.05,
        PGuess = 0.20,
        PForget = 0.02,
        PInitial = 0.10,
    };

    [Fact]
    public void ValidDefaults_ConvertCleanly()
    {
        var p = Valid().ToBktParameters();
        Assert.Equal(0.10, p.PLearning, precision: 6);
        Assert.Equal(0.05, p.PSlip, precision: 6);
        Assert.Equal(0.20, p.PGuess, precision: 6);
        Assert.Equal(0.02, p.PForget, precision: 6);
        Assert.Equal(0.10, p.PInitial, precision: 6);
    }

    [Theory]
    [InlineData(0.0)]     // lower-bound exclusive
    [InlineData(1.0)]     // upper-bound exclusive
    [InlineData(-0.1)]    // negative
    [InlineData(1.5)]     // out-of-range
    public void PLearning_OutOfOpenInterval_Throws(double bad)
    {
        var p = Valid();
        p.PLearning = bad;
        Assert.Throws<InvalidOperationException>(() => p.ToBktParameters());
    }

    [Fact]
    public void PLearning_NaN_Throws()
    {
        var p = Valid();
        p.PLearning = double.NaN;
        Assert.Throws<InvalidOperationException>(() => p.ToBktParameters());
    }

    [Fact]
    public void PLearning_Infinity_Throws()
    {
        var p = Valid();
        p.PLearning = double.PositiveInfinity;
        Assert.Throws<InvalidOperationException>(() => p.ToBktParameters());
    }

    [Fact]
    public void DegenerateSlipGuess_Throws()
    {
        // PSlip + PGuess = 1.0 inverts BKT inference; must reject.
        var p = Valid();
        p.PSlip = 0.6;
        p.PGuess = 0.4;
        var ex = Assert.Throws<InvalidOperationException>(() => p.ToBktParameters());
        Assert.Contains("degenerate", ex.Message);
    }

    [Fact]
    public void ForgetExceedsLearning_Throws()
    {
        var p = Valid();
        p.PForget = 0.5;
        p.PLearning = 0.1;
        var ex = Assert.Throws<InvalidOperationException>(() => p.ToBktParameters());
        Assert.Contains("unstable", ex.Message);
    }

    [Fact]
    public void EveryProbabilityChecked_NotJustPLearning()
    {
        // Spot-check that each of the 5 fields independently trips the
        // range validator — guards against future code that forgets to
        // validate a newly-added field.
        foreach (var mutate in new Action<SubjectBktParams>[]
        {
            p => p.PSlip = 0,
            p => p.PGuess = 1.0,
            p => p.PForget = double.NaN,
            p => p.PInitial = 2.0,
        })
        {
            var p = Valid();
            mutate(p);
            Assert.Throws<InvalidOperationException>(() => p.ToBktParameters());
        }
    }
}

public class BktCalibrationConfigFileTests
{
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repo root not found");
    }

    [Fact]
    public void ConfigFile_IsValid_AllSubjectsPassValidation()
    {
        var repoRoot = FindRepoRoot();
        var path = Path.Combine(repoRoot, "config", "bkt-params.json");
        Assert.True(File.Exists(path), $"config/bkt-params.json missing at {path}");

        var json = File.ReadAllText(path);
        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.TryGetProperty("BktCalibration", out var section),
            "bkt-params.json missing 'BktCalibration' root section");

        // Deserialise through the same binding shape production uses.
        var opts = JsonSerializer.Deserialize<BktCalibrationOptions>(
            section.GetRawText(),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(opts);

        // Defaults must pass.
        Assert.NotNull(opts!.Defaults.ToBktParameters());

        // Every subject-specific override must pass.
        foreach (var (subject, p) in opts.Subjects)
        {
            var converted = p.ToBktParameters();
            Assert.True(converted.PLearning > 0, $"subject '{subject}' produced invalid PLearning");
        }
    }

    [Fact]
    public void ConfigFile_SchemaVersion_Is1()
    {
        // A schema bump needs to be explicit — not a silent drift from
        // a migration. Future version 2 lands with an accompanying
        // migration + test update.
        var repoRoot = FindRepoRoot();
        var path = Path.Combine(repoRoot, "config", "bkt-params.json");
        var json = File.ReadAllText(path);
        using var doc = JsonDocument.Parse(json);
        var section = doc.RootElement.GetProperty("BktCalibration");
        var version = section.GetProperty("version").GetInt32();
        Assert.Equal(1, version);
    }
}
