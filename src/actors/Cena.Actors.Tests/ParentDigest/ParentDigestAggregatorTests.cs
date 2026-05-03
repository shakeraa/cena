// =============================================================================
// Cena Platform — ParentDigestAggregator unit tests (RDY-067 F5a Phase 1).
//
// Covers the aggregator's invariants:
//   - Empty week → TookABreak=true row per minor, zero counters
//   - Partial week → correct totals; mean-of-deltas mastery math
//   - Multi-minor parent → one row per linked minor, consistent envelope
//   - Events outside [weekStart, weekEnd) are filtered out
// =============================================================================

using Cena.Actors.ParentDigest;

namespace Cena.Actors.Tests.ParentDigest;

public sealed class ParentDigestAggregatorTests
{
    private static readonly DateTimeOffset WeekStart =
        new(2026, 4, 13, 0, 0, 0, TimeSpan.FromHours(3)); // Mon 00:00 Asia/Jerusalem
    private static readonly DateTimeOffset WeekEnd =
        new(2026, 4, 20, 0, 0, 0, TimeSpan.FromHours(3)); // Following Mon 00:00 (exclusive)

    private static readonly MinorContext MinorA =
        new(Guid.Parse("11111111-1111-1111-1111-111111111111"), "Your 11th-grader");
    private static readonly MinorContext MinorB =
        new(Guid.Parse("22222222-2222-2222-2222-222222222222"), "Your 9th-grader");

    [Fact]
    public void EmptyWeek_SingleMinor_ProducesTookABreakRow()
    {
        var env = ParentDigestAggregator.BuildEnvelope(
            parentFirstName: "Rachel",
            parentLocale: DigestLocale.En,
            weekStart: WeekStart,
            weekEnd: WeekEnd,
            minors: new[] { MinorA },
            events: Array.Empty<IDigestSourceEvent>());

        Assert.Single(env.Rows);
        var row = env.Rows[0];

        Assert.True(row.TookABreak);
        Assert.Equal(0.0, row.HoursStudied);
        Assert.Empty(row.TopicsCovered);
        Assert.Equal(0.0, row.MasteryGain);
        Assert.Equal(0, row.SessionCount);
        Assert.Equal("Your 11th-grader", row.MinorLabel);
    }

    [Fact]
    public void PartialWeek_AggregatesHoursTopicsSessionsAndMasteryDelta()
    {
        var t0 = WeekStart.AddHours(2);
        var events = new IDigestSourceEvent[]
        {
            new DigestStudyMinutesEvent(MinorA.MinorId, t0, 45),
            new DigestStudyMinutesEvent(MinorA.MinorId, t0.AddHours(1), 30),
            new DigestTopicTouchedEvent(MinorA.MinorId, t0, "calc.integration"),
            new DigestTopicTouchedEvent(MinorA.MinorId, t0.AddHours(1), "calc.chain-rule"),
            new DigestTopicTouchedEvent(MinorA.MinorId, t0.AddHours(2), "calc.integration"),
            new DigestMasteryObservedEvent(MinorA.MinorId, t0, "calc.integration", 0.50),
            new DigestMasteryObservedEvent(MinorA.MinorId, t0.AddHours(2), "calc.integration", 0.70),
            new DigestMasteryObservedEvent(MinorA.MinorId, t0, "calc.chain-rule", 0.30),
            new DigestMasteryObservedEvent(MinorA.MinorId, t0.AddHours(1), "calc.chain-rule", 0.40),
            new DigestSessionCompletedEvent(MinorA.MinorId, t0.AddHours(1)),
            new DigestSessionCompletedEvent(MinorA.MinorId, t0.AddHours(3)),
        };

        var env = ParentDigestAggregator.BuildEnvelope(
            parentFirstName: "Rachel",
            parentLocale: DigestLocale.En,
            weekStart: WeekStart,
            weekEnd: WeekEnd,
            minors: new[] { MinorA },
            events: events);

        Assert.Single(env.Rows);
        var row = env.Rows[0];

        Assert.False(row.TookABreak);
        Assert.Equal(1.25, row.HoursStudied); // (45 + 30) min / 60
        Assert.Equal(new[] { "calc.chain-rule", "calc.integration" }, row.TopicsCovered);
        Assert.Equal(2, row.SessionCount);
        // calc.integration: 0.70 - 0.50 = 0.20; calc.chain-rule: 0.40 - 0.30 = 0.10
        // mean = 0.15
        Assert.Equal(0.15, row.MasteryGain, 3);
    }

    [Fact]
    public void EventsOutsideWindow_AreFiltered()
    {
        var events = new IDigestSourceEvent[]
        {
            // Outside: just before window start
            new DigestStudyMinutesEvent(MinorA.MinorId, WeekStart.AddMinutes(-1), 120),
            // Outside: exactly at window end (exclusive)
            new DigestStudyMinutesEvent(MinorA.MinorId, WeekEnd, 60),
            // Inside
            new DigestStudyMinutesEvent(MinorA.MinorId, WeekStart.AddHours(1), 30),
        };

        var env = ParentDigestAggregator.BuildEnvelope(
            parentFirstName: "Rachel",
            parentLocale: DigestLocale.En,
            weekStart: WeekStart,
            weekEnd: WeekEnd,
            minors: new[] { MinorA },
            events: events);

        Assert.Equal(0.5, env.Rows[0].HoursStudied); // only the 30 min inside counts
    }

    [Fact]
    public void MultiMinorParent_ProducesOneRowPerMinorUnderSameEnvelope()
    {
        var t0 = WeekStart.AddHours(2);
        var events = new IDigestSourceEvent[]
        {
            new DigestStudyMinutesEvent(MinorA.MinorId, t0, 60),
            new DigestSessionCompletedEvent(MinorA.MinorId, t0),
            // MinorB has no events -> TookABreak row
        };

        var env = ParentDigestAggregator.BuildEnvelope(
            parentFirstName: "Rachel",
            parentLocale: DigestLocale.En,
            weekStart: WeekStart,
            weekEnd: WeekEnd,
            minors: new[] { MinorA, MinorB },
            events: events);

        Assert.Equal(2, env.Rows.Count);
        Assert.Equal("Your 11th-grader", env.Rows[0].MinorLabel);
        Assert.False(env.Rows[0].TookABreak);
        Assert.Equal("Your 9th-grader", env.Rows[1].MinorLabel);
        Assert.True(env.Rows[1].TookABreak);
        Assert.Equal("Rachel", env.ParentFirstName);
        Assert.Equal(DigestLocale.En, env.ParentLocale);
    }

    [Fact]
    public void SingleMasteryObservationPerTopic_ContributesZeroDelta()
    {
        // Only one observation — no growth evidence. Aggregator must NOT
        // synthesise growth from a single datapoint.
        var t0 = WeekStart.AddHours(2);
        var events = new IDigestSourceEvent[]
        {
            new DigestStudyMinutesEvent(MinorA.MinorId, t0, 30),
            new DigestMasteryObservedEvent(MinorA.MinorId, t0, "calc.integration", 0.80),
        };

        var env = ParentDigestAggregator.BuildEnvelope(
            parentFirstName: "Rachel",
            parentLocale: DigestLocale.En,
            weekStart: WeekStart,
            weekEnd: WeekEnd,
            minors: new[] { MinorA },
            events: events);

        Assert.Equal(0.0, env.Rows[0].MasteryGain);
    }

    [Fact]
    public void InvalidWindow_WeekEndBeforeWeekStart_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            ParentDigestAggregator.BuildEnvelope(
                parentFirstName: "Rachel",
                parentLocale: DigestLocale.En,
                weekStart: WeekEnd,
                weekEnd: WeekStart,
                minors: new[] { MinorA },
                events: Array.Empty<IDigestSourceEvent>()));
    }

    [Fact]
    public void DuplicateTopicTouches_DoNotInflateTopicList()
    {
        var t0 = WeekStart.AddHours(2);
        var events = new IDigestSourceEvent[]
        {
            new DigestTopicTouchedEvent(MinorA.MinorId, t0, "calc.integration"),
            new DigestTopicTouchedEvent(MinorA.MinorId, t0.AddMinutes(10), "calc.integration"),
            new DigestTopicTouchedEvent(MinorA.MinorId, t0.AddMinutes(20), "calc.integration"),
            new DigestStudyMinutesEvent(MinorA.MinorId, t0, 10),
        };

        var env = ParentDigestAggregator.BuildEnvelope(
            parentFirstName: "Rachel",
            parentLocale: DigestLocale.En,
            weekStart: WeekStart,
            weekEnd: WeekEnd,
            minors: new[] { MinorA },
            events: events);

        Assert.Equal(new[] { "calc.integration" }, env.Rows[0].TopicsCovered);
    }
}
