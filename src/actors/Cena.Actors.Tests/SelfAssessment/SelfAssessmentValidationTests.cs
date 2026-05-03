// =============================================================================
// Cena Platform — SelfAssessmentEndpoints validation tests (RDY-057)
//
// Exercises the pure-function TryValidate helper exposed via
// InternalsVisibleTo. These tests lock the server-side contract that the
// Vue SelfAssessmentStep relies on: Likert range, free-text cap, tag
// shape (lowercase kebab), TopicFeeling enum parse.
// =============================================================================

using Cena.Api.Host.Endpoints;

namespace Cena.Actors.Tests.SelfAssessment;

public class SelfAssessmentValidationTests
{
    private static SelfAssessmentRequestDto WellFormed(
        Dictionary<string, int>? conf = null,
        List<string>? strengths = null,
        List<string>? friction = null,
        Dictionary<string, string>? feelings = null,
        string? freeText = null,
        bool skipped = false,
        bool optIn = false) => new(
            Skipped: skipped,
            SubjectConfidence: conf,
            Strengths: strengths,
            FrictionPoints: friction,
            TopicFeelings: feelings,
            FreeText: freeText,
            OptInPersistent: optIn);

    [Fact]
    public void Minimal_SkippedRequest_IsValid()
    {
        var req = WellFormed(skipped: true);
        Assert.True(SelfAssessmentEndpoints.TryValidate(req, out _));
    }

    [Fact]
    public void Likert_InRange_Accepted()
    {
        var req = WellFormed(conf: new() { ["algebra"] = 1, ["calculus"] = 5 });
        Assert.True(SelfAssessmentEndpoints.TryValidate(req, out _));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(-1)]
    [InlineData(100)]
    public void Likert_OutOfRange_Rejected(int bad)
    {
        var req = WellFormed(conf: new() { ["algebra"] = bad });
        var ok = SelfAssessmentEndpoints.TryValidate(req, out var err);
        Assert.False(ok);
        Assert.Contains("1..5", err);
    }

    [Fact]
    public void EmptySubjectKey_Rejected()
    {
        var req = WellFormed(conf: new() { [""] = 3 });
        Assert.False(SelfAssessmentEndpoints.TryValidate(req, out _));
    }

    [Fact]
    public void FreeText_Exactly200_Accepted()
    {
        var req = WellFormed(freeText: new string('a', 200));
        Assert.True(SelfAssessmentEndpoints.TryValidate(req, out _));
    }

    [Fact]
    public void FreeText_Over200_Rejected()
    {
        var req = WellFormed(freeText: new string('a', 201));
        var ok = SelfAssessmentEndpoints.TryValidate(req, out var err);
        Assert.False(ok);
        Assert.Contains("200", err);
    }

    [Fact]
    public void ValidTopicFeeling_Accepted()
    {
        foreach (var f in new[] { "Solid", "Unsure", "Anxious", "New" })
        {
            var req = WellFormed(feelings: new() { ["algebra"] = f });
            Assert.True(SelfAssessmentEndpoints.TryValidate(req, out _),
                $"'{f}' should be accepted");
        }
    }

    [Fact]
    public void UnknownTopicFeeling_Rejected()
    {
        var req = WellFormed(feelings: new() { ["algebra"] = "HappyPath" });
        var ok = SelfAssessmentEndpoints.TryValidate(req, out var err);
        Assert.False(ok);
        Assert.Contains("HappyPath", err);
    }

    [Fact]
    public void Strengths_LowercaseKebab_Accepted()
    {
        var req = WellFormed(strengths: new() { "visualizer", "step-by-step", "a1-b2" });
        Assert.True(SelfAssessmentEndpoints.TryValidate(req, out _));
    }

    [Theory]
    [InlineData("Visualizer")]      // uppercase
    [InlineData("step by step")]    // space
    [InlineData("formula_memorizer")] // underscore
    [InlineData("bad.tag")]         // dot
    public void Strengths_NonKebabShape_Rejected(string bad)
    {
        var req = WellFormed(strengths: new() { bad });
        Assert.False(SelfAssessmentEndpoints.TryValidate(req, out _));
    }

    [Fact]
    public void Tag_Over48Chars_Rejected()
    {
        var req = WellFormed(strengths: new() { new string('a', 49) });
        Assert.False(SelfAssessmentEndpoints.TryValidate(req, out _));
    }

    [Fact]
    public void FrictionPoints_UseSameTagRules()
    {
        // Friction list goes through the same tag validator as strengths.
        var req = WellFormed(friction: new() { "Word-Problems" });  // uppercase
        Assert.False(SelfAssessmentEndpoints.TryValidate(req, out _));
    }

    [Fact]
    public void NullRequestBody_Rejected()
    {
        Assert.False(SelfAssessmentEndpoints.TryValidate(null!, out _));
    }

    [Fact]
    public void AllFieldsNullButSkippedFalse_Valid()
    {
        // A minimal empty POST (SPA submitting a cleared assessment) is
        // permitted — the server stores it and the 90-day timer starts.
        var req = WellFormed(skipped: false);
        Assert.True(SelfAssessmentEndpoints.TryValidate(req, out _));
    }
}
