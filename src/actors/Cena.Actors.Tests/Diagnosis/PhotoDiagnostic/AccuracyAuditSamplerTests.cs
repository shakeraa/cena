// =============================================================================
// Cena Platform — AccuracyAuditSampler tests (EPIC-PRR-J PRR-423)
// =============================================================================

using System.Diagnostics.Metrics;
using Cena.Actors.Diagnosis.PhotoDiagnostic;
using Xunit;

namespace Cena.Actors.Tests.Diagnosis.PhotoDiagnostic;

public class AccuracyAuditSamplerTests
{
    private static PhotoDiagnosticMetrics NewMetrics() =>
        new(new DummyMeterFactory());

    private sealed class DummyMeterFactory : IMeterFactory
    {
        public Meter Create(MeterOptions options) => new(options);
        public void Dispose() { }
    }

    private static AccuracyAuditCandidate Candidate(
        string id,
        double ocr = 0.9,
        double? tpl = 0.9,
        bool matched = true,
        bool chainOk = true,
        MisconceptionBreakType? breakType = MisconceptionBreakType.SignFlipDistributive) =>
        new(id, "student-hash-xyz", breakType, ocr, tpl, matched, chainOk);

    [Fact]
    public void LowOcrConfidenceAlwaysSampled()
    {
        var sampler = new AccuracyAuditSampler(NewMetrics());
        var decision = sampler.Decide(Candidate("d-100", ocr: 0.3));
        Assert.True(decision.Sampled);
        Assert.Equal("low_ocr_confidence", decision.Reason);
    }

    [Fact]
    public void NoTemplateMatchAlwaysSampled()
    {
        var sampler = new AccuracyAuditSampler(NewMetrics());
        var decision = sampler.Decide(Candidate("d-101", matched: false, tpl: null, breakType: null));
        Assert.True(decision.Sampled);
        Assert.Equal("no_template_match", decision.Reason);
    }

    [Fact]
    public void SamplerIsDeterministicForSameId()
    {
        var sampler = new AccuracyAuditSampler(NewMetrics());
        var first = sampler.Decide(Candidate("d-deterministic"));
        var second = sampler.Decide(Candidate("d-deterministic"));
        Assert.Equal(first.Sampled, second.Sampled);
        Assert.Equal(first.Reason, second.Reason);
    }

    [Fact]
    public void HashBucketIsInZeroToThousand()
    {
        for (int i = 0; i < 200; i++)
        {
            var b = AccuracyAuditSampler.HashBucketPermille($"diag-{i:D6}");
            Assert.InRange(b, 0, 999);
        }
    }

    [Fact]
    public void RandomSampleRateLandsInRoughly5PercentBand()
    {
        var sampler = new AccuracyAuditSampler(NewMetrics());
        int sampled = 0;
        const int n = 2000;
        for (int i = 0; i < n; i++)
        {
            var decision = sampler.Decide(Candidate($"d-uniform-{i:D6}"));
            if (decision.Sampled) sampled++;
        }
        // Target is 5% = 100/2000. SHA256-based distribution should land in
        // a wide band around that; we tolerate 1.5%..10% to keep CI stable
        // against hash distribution quirks. This guards regression, not
        // exact statistical calibration.
        Assert.InRange(sampled, 30, 200);
    }

    [Fact]
    public void EmptyDiagnosticIdThrows()
    {
        var sampler = new AccuracyAuditSampler(NewMetrics());
        Assert.Throws<ArgumentException>(() => sampler.Decide(Candidate("")));
    }

    [Fact]
    public void NullCandidateThrows()
    {
        var sampler = new AccuracyAuditSampler(NewMetrics());
        Assert.Throws<ArgumentNullException>(() => sampler.Decide(null!));
    }
}
