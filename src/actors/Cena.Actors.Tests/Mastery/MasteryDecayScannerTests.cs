// =============================================================================
// MST-006/MST-007 Tests: Decay scanner and stagnation detection
// =============================================================================

using Cena.Actors.Mastery;

namespace Cena.Actors.Tests.Mastery;

public sealed class MasteryDecayScannerTests
{
    [Fact]
    public void Scan_MasteredConceptForgotten_ReturnsDecayResult()
    {
        var now = DateTimeOffset.UtcNow;
        var overlay = new Dictionary<string, ConceptMasteryState>
        {
            ["quadratics"] = new()
            {
                MasteryProbability = 0.95f,
                HalfLifeHours = 168f, // 1 week
                LastInteraction = now.AddDays(-14), // 2 weeks ago
                AttemptCount = 20,
                CorrectCount = 18
            }
        };

        var results = MasteryDecayScanner.Scan(overlay, now);

        var decay = Assert.Single(results);
        Assert.Equal("quadratics", decay.ConceptId);
        Assert.InRange(decay.RecallProbability, 0.24f, 0.26f); // 2^(-336/168) = 0.25
    }

    [Fact]
    public void Scan_RecentlyPracticed_NoDecay()
    {
        var now = DateTimeOffset.UtcNow;
        var overlay = new Dictionary<string, ConceptMasteryState>
        {
            ["trigonometry"] = new()
            {
                MasteryProbability = 0.92f,
                HalfLifeHours = 168f,
                LastInteraction = now.AddHours(-2),
                AttemptCount = 15,
                CorrectCount = 14
            }
        };

        var results = MasteryDecayScanner.Scan(overlay, now);

        Assert.Empty(results);
    }

    [Fact]
    public void Scan_LowMastery_NotScanned()
    {
        var now = DateTimeOffset.UtcNow;
        var overlay = new Dictionary<string, ConceptMasteryState>
        {
            ["complex-analysis"] = new()
            {
                MasteryProbability = 0.40f,
                HalfLifeHours = 48f,
                LastInteraction = now.AddDays(-30)
            }
        };

        var results = MasteryDecayScanner.Scan(overlay, now);

        Assert.Empty(results); // mastery < 0.70
    }

    [Fact]
    public void Scan_MultipleDecaying_ReturnsAll()
    {
        var now = DateTimeOffset.UtcNow;
        var twoWeeksAgo = now.AddDays(-14);

        var overlay = new Dictionary<string, ConceptMasteryState>
        {
            ["concept-A"] = new() { MasteryProbability = 0.92f, HalfLifeHours = 72f, LastInteraction = twoWeeksAgo },
            ["concept-B"] = new() { MasteryProbability = 0.88f, HalfLifeHours = 48f, LastInteraction = twoWeeksAgo },
            ["concept-C"] = new() { MasteryProbability = 0.95f, HalfLifeHours = 2000f, LastInteraction = twoWeeksAgo }
        };

        var results = MasteryDecayScanner.Scan(overlay, now);

        // A and B decayed (short half-life), C has long half-life (2000h) -> recall ~0.89 still OK
        Assert.Equal(2, results.Count);
        Assert.Contains(results, r => r.ConceptId == "concept-A");
        Assert.Contains(results, r => r.ConceptId == "concept-B");
    }

    [Fact]
    public void Scan_ZeroHalfLife_Skipped()
    {
        var now = DateTimeOffset.UtcNow;
        var overlay = new Dictionary<string, ConceptMasteryState>
        {
            ["broken"] = new()
            {
                MasteryProbability = 0.90f,
                HalfLifeHours = 0f,
                LastInteraction = now.AddDays(-1)
            }
        };

        var results = MasteryDecayScanner.Scan(overlay, now);

        Assert.Empty(results); // half-life invalid
    }
}

public sealed class MasteryStagnationDetectorTests
{
    [Fact]
    public void DetectDominantError_ThreeOfSameType_ReturnsType()
    {
        var state = new ConceptMasteryState
        {
            RecentErrors = new[]
            {
                ErrorType.Procedural,
                ErrorType.Conceptual,
                ErrorType.Procedural,
                ErrorType.Procedural
            }
        };

        var dominant = MasteryStagnationDetector.DetectDominantError(state);

        Assert.Equal(ErrorType.Procedural, dominant);
    }

    [Fact]
    public void DetectDominantError_NoRepeats_ReturnsNull()
    {
        var state = new ConceptMasteryState
        {
            RecentErrors = new[]
            {
                ErrorType.Procedural,
                ErrorType.Conceptual,
                ErrorType.Careless
            }
        };

        var dominant = MasteryStagnationDetector.DetectDominantError(state);

        Assert.Null(dominant);
    }

    [Fact]
    public void DetectDominantError_TooFewErrors_ReturnsNull()
    {
        var state = new ConceptMasteryState
        {
            RecentErrors = new[] { ErrorType.Procedural, ErrorType.Procedural }
        };

        var dominant = MasteryStagnationDetector.DetectDominantError(state);

        Assert.Null(dominant);
    }

    [Fact]
    public void DetectDominantError_EmptyErrors_ReturnsNull()
    {
        var state = new ConceptMasteryState();
        Assert.Null(MasteryStagnationDetector.DetectDominantError(state));
    }
}
