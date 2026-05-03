using System.Diagnostics.Metrics;
using Cena.Actors.Services;
using NSubstitute;

namespace Cena.Actors.Tests.Services;

/// <summary>
/// FOC-012: Cultural Resilience Stratification tests.
/// Covers cultural context detection, resilience weight adjustment,
/// and social resilience signal interface.
/// </summary>
public sealed class CulturalResilienceTests
{
    private readonly CulturalContextService _contextService = new();
    private readonly FocusDegradationService _focusService;
    private readonly SocialResilienceSignal _socialSignal = new();

    public CulturalResilienceTests()
    {
        var meterFactory = Substitute.For<IMeterFactory>();
        meterFactory.Create(Arg.Any<MeterOptions>()).Returns(new Meter("test"));
        _focusService = new FocusDegradationService(meterFactory);
    }

    // ═══════════════════════════════════════════════════════════════
    // FOC-012.1: Cultural Context Detection
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Detect_HebrewOnboarding_HebrewDominant()
    {
        var input = new CulturalContextInput("he", null, null);
        Assert.Equal(CulturalContext.HebrewDominant, _contextService.Detect(input));
    }

    [Fact]
    public void Detect_ArabicOnboarding_ArabicDominant()
    {
        var input = new CulturalContextInput("ar", null, null);
        Assert.Equal(CulturalContext.ArabicDominant, _contextService.Detect(input));
    }

    [Fact]
    public void Detect_BothLanguages_Bilingual()
    {
        var input = new CulturalContextInput("he", "ar", null);
        Assert.Equal(CulturalContext.Bilingual, _contextService.Detect(input));
    }

    [Fact]
    public void Detect_NoLanguage_Unknown()
    {
        var input = new CulturalContextInput(null, null, null);
        Assert.Equal(CulturalContext.Unknown, _contextService.Detect(input));
    }

    [Fact]
    public void Detect_ArabicInterface_ArabicDominant()
    {
        var input = new CulturalContextInput(null, "ar", null);
        Assert.Equal(CulturalContext.ArabicDominant, _contextService.Detect(input));
    }

    [Fact]
    public void Detect_HebrewTyping_HebrewDominant()
    {
        var input = new CulturalContextInput(null, null, "he");
        Assert.Equal(CulturalContext.HebrewDominant, _contextService.Detect(input));
    }

    [Fact]
    public void Detect_CaseInsensitive()
    {
        var input = new CulturalContextInput("HE", "AR", null);
        Assert.Equal(CulturalContext.Bilingual, _contextService.Detect(input));
    }

    // ═══════════════════════════════════════════════════════════════
    // FOC-012.2: Resilience Weight Adjustment
    // ═══════════════════════════════════════════════════════════════

    private static ResilienceInput MakeResilienceInput(CulturalContext ctx) => new(
        TotalSessionsStarted: 20,
        SessionsCompletedNormally: 16,
        BadSessionCount: 5,
        ReturnedAfterBadSession: 5,  // 100% recovery
        TotalAttempts: 200,
        AttemptsAboveComfortZone: 60,
        CurrentStreak: 7,
        LongestStreak: 10,
        CulturalContext: ctx
    );

    [Fact]
    public void Resilience_ArabicDominant_HigherRecoveryWeight()
    {
        // Student with perfect recovery rate (5/5) should score higher
        // under Arabic weights (recovery=0.30) vs Hebrew weights (recovery=0.25)
        var arabicInput = MakeResilienceInput(CulturalContext.ArabicDominant);
        var hebrewInput = MakeResilienceInput(CulturalContext.HebrewDominant);

        var arabicScore = _focusService.ComputeResilience(arabicInput);
        var hebrewScore = _focusService.ComputeResilience(hebrewInput);

        // With 100% recovery, higher recovery weight → higher score for Arabic
        Assert.True(arabicScore.Score >= hebrewScore.Score,
            $"Arabic ({arabicScore.Score:F3}) should >= Hebrew ({hebrewScore.Score:F3}) with perfect recovery");
    }

    [Fact]
    public void Resilience_ArabicDominant_LowerPersistenceWeight()
    {
        // Student with high persistence but low recovery → Hebrew weights favor them
        var input = new ResilienceInput(
            TotalSessionsStarted: 20,
            SessionsCompletedNormally: 19,  // 95% persistence
            BadSessionCount: 5,
            ReturnedAfterBadSession: 1,     // 20% recovery — low
            TotalAttempts: 200,
            AttemptsAboveComfortZone: 60,
            CurrentStreak: 7,
            LongestStreak: 10,
            CulturalContext: CulturalContext.ArabicDominant
        );

        var hebrewInput = input with { CulturalContext = CulturalContext.HebrewDominant };

        var arabicScore = _focusService.ComputeResilience(input);
        var hebrewScore = _focusService.ComputeResilience(hebrewInput);

        // With low recovery, Arabic weights (lower persistence, higher recovery) should be lower
        Assert.True(hebrewScore.Score >= arabicScore.Score,
            $"Hebrew ({hebrewScore.Score:F3}) should >= Arabic ({arabicScore.Score:F3}) with low recovery");
    }

    [Fact]
    public void Resilience_BilingualAndUnknown_UseBaselineWeights()
    {
        var bilingualInput = MakeResilienceInput(CulturalContext.Bilingual);
        var unknownInput = MakeResilienceInput(CulturalContext.Unknown);
        var hebrewInput = MakeResilienceInput(CulturalContext.HebrewDominant);

        var bilingualScore = _focusService.ComputeResilience(bilingualInput);
        var unknownScore = _focusService.ComputeResilience(unknownInput);
        var hebrewScore = _focusService.ComputeResilience(hebrewInput);

        // Bilingual and Unknown both use baseline (same as Hebrew)
        Assert.Equal(hebrewScore.Score, bilingualScore.Score, precision: 6);
        Assert.Equal(hebrewScore.Score, unknownScore.Score, precision: 6);
    }

    [Fact]
    public void Resilience_DefaultCulturalContext_IsUnknown()
    {
        // When no cultural context is specified, default is Unknown (baseline weights)
        var input = new ResilienceInput(
            TotalSessionsStarted: 10,
            SessionsCompletedNormally: 8,
            BadSessionCount: 2,
            ReturnedAfterBadSession: 2,
            TotalAttempts: 100,
            AttemptsAboveComfortZone: 30,
            CurrentStreak: 5,
            LongestStreak: 10
        );

        Assert.Equal(CulturalContext.Unknown, input.CulturalContext);
        var score = _focusService.ComputeResilience(input);
        Assert.InRange(score.Score, 0.0, 1.0);
    }

    // ═══════════════════════════════════════════════════════════════
    // FOC-012.3: Social Resilience Signal
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void SocialResilienceSignal_ReturnsNull_NotYetImplemented()
    {
        var input = new SocialResilienceInput(
            StudentId: Guid.NewGuid(),
            StudyGroupParticipationCount: 5,
            PeerHelpGivenCount: 3,
            PeerHelpReceivedCount: 4,
            SharedSessionCompletions: 2
        );

        var result = _socialSignal.ComputeSocialScore(input);
        Assert.Null(result);
    }
}
