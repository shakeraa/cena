// =============================================================================
// Cena Platform — BKT reference-anchor discount tests (PRR-261, ADR-0059 §15.9 R12)
//
// Three test layers, all unit-scope (no Marten, no I/O):
//
//   1. Pure-math: BktTracer.UpdateWithDiscount at factor=1 is bit-identical
//      to BktTracer.Update (so non-anchored callers see no behaviour change),
//      and at factor<1 produces a posterior pulled toward the prior.
//
//   2. Policy: BktReferenceAnchorDiscountPolicy.Default returns 0.5 inside
//      the 5-minute window, 1.0 outside, 1.0 for null/negative input.
//      Constants are pinned (a future spec change should be a deliberate
//      edit, not a typo).
//
//   3. Tracker integration: a full-weight attempt vs. a reference-anchored
//      attempt produce DIFFERENT posteriors; the audit fields land on the
//      emitted MasteryUpdated_V2 event; spacing-benefit (ADR-0050 Item 4)
//      is preserved (the attempt still updates the mastery row, just
//      half-weighted).
// =============================================================================

using Cena.Actors.ExamTargets;
using Cena.Actors.Mastery;
using Cena.Actors.Mastery.Events;
using Xunit;

namespace Cena.Actors.Tests.Mastery;

public sealed class BktReferenceAnchorDiscountTests
{
    private sealed class CollectingSink : IMasteryEventSink
    {
        public List<MasteryUpdated_V2> Events { get; } = new();
        public Task AppendAsync(MasteryUpdated_V2 @event, CancellationToken ct = default)
        {
            Events.Add(@event);
            return Task.CompletedTask;
        }
    }

    // ── pure-math layer ─────────────────────────────────────────────────────

    [Fact]
    public void UpdateWithDiscount_at_factor_1_is_bit_identical_to_Update()
    {
        // Property: non-anchored attempts (factor=1) MUST behave exactly like
        // the legacy BktTracer.Update. Any drift here would silently change
        // every existing call site that hasn't been updated to pass timing.
        var p = BktParameters.Default;
        foreach (var prior in new[] { 0.05f, 0.25f, 0.5f, 0.75f, 0.95f })
        {
            foreach (var correct in new[] { true, false })
            {
                var withDiscount = BktTracer.UpdateWithDiscount(prior, correct, p, 1f);
                var legacy = BktTracer.Update(prior, correct, p);
                Assert.Equal(legacy, withDiscount);
            }
        }
    }

    [Fact]
    public void UpdateWithDiscount_at_factor_zero_only_applies_learning_transition()
    {
        // factor=0 means "ignore the observation entirely"; the only delta
        // from prior should be the learning transition P_T. Useful as a
        // sanity-check for the math (and a cheap way to detect a bug in
        // the discount-blend formula).
        var p = BktParameters.Default;
        var prior = 0.4f;

        var correct = BktTracer.UpdateWithDiscount(prior, true, p, 0f);
        var incorrect = BktTracer.UpdateWithDiscount(prior, false, p, 0f);

        Assert.Equal(correct, incorrect);
        var expected = Math.Clamp(prior + (1f - prior) * p.P_T, 0.001f, 0.999f);
        Assert.Equal(expected, correct, precision: 6);
    }

    [Fact]
    public void UpdateWithDiscount_at_factor_half_lands_between_prior_and_full_posterior()
    {
        // The actual core property: at factor=0.5, the discounted posterior
        // is the midpoint between prior-only-transition and full-Bayesian-update.
        var p = BktParameters.Default;
        var prior = 0.3f;

        var full = BktTracer.UpdateWithDiscount(prior, true, p, 1f);
        var half = BktTracer.UpdateWithDiscount(prior, true, p, 0.5f);
        var none = BktTracer.UpdateWithDiscount(prior, true, p, 0f);

        // Strictly between (correct attempt → full > half > none).
        Assert.True(half < full, $"half={half} should be < full={full}");
        Assert.True(half > none, $"half={half} should be > none={none}");
    }

    [Fact]
    public void UpdateWithDiscount_clamps_factor_to_unit_interval()
    {
        var p = BktParameters.Default;
        var prior = 0.5f;

        var clampedHigh = BktTracer.UpdateWithDiscount(prior, true, p, 5f);
        var atOne = BktTracer.UpdateWithDiscount(prior, true, p, 1f);
        var clampedLow = BktTracer.UpdateWithDiscount(prior, true, p, -1f);
        var atZero = BktTracer.UpdateWithDiscount(prior, true, p, 0f);

        Assert.Equal(atOne, clampedHigh);
        Assert.Equal(atZero, clampedLow);
    }

    // ── policy layer ────────────────────────────────────────────────────────

    [Fact]
    public void Policy_returns_anchored_factor_inside_window()
    {
        var policy = BktReferenceAnchorDiscountPolicy.Default;

        // 0 seconds (just rendered) and 300 (boundary, inclusive) are both anchored.
        Assert.Equal(0.5f, policy.ResolveDiscountFactor(0));
        Assert.Equal(0.5f, policy.ResolveDiscountFactor(60));
        Assert.Equal(0.5f, policy.ResolveDiscountFactor(150));
        Assert.Equal(0.5f, policy.ResolveDiscountFactor(300));
    }

    [Fact]
    public void Policy_returns_unanchored_factor_outside_window()
    {
        var policy = BktReferenceAnchorDiscountPolicy.Default;

        Assert.Equal(1.0f, policy.ResolveDiscountFactor(301));
        Assert.Equal(1.0f, policy.ResolveDiscountFactor(600));
        Assert.Equal(1.0f, policy.ResolveDiscountFactor(int.MaxValue));
    }

    [Fact]
    public void Policy_treats_null_or_negative_as_unanchored()
    {
        // null = caller has no recent reference / doesn't track timing.
        // negative = nonsensical input; safe default to full weight.
        var policy = BktReferenceAnchorDiscountPolicy.Default;

        Assert.Equal(1.0f, policy.ResolveDiscountFactor(null));
        Assert.Equal(1.0f, policy.ResolveDiscountFactor(-1));
        Assert.Equal(1.0f, policy.ResolveDiscountFactor(-9999));
    }

    [Fact]
    public void Policy_constants_are_pinned()
    {
        // Window + factor are ADR-0059 §15.9 R12 invariants; future tuning
        // should require a deliberate code change visible in this test.
        Assert.Equal(300, BktReferenceAnchorDiscountPolicy.WindowSeconds);
        Assert.Equal(0.5f, BktReferenceAnchorDiscountPolicy.AnchoredFactor);
        Assert.Equal(1.0f, BktReferenceAnchorDiscountPolicy.UnanchoredFactor);
    }

    // ── tracker integration ─────────────────────────────────────────────────

    [Fact]
    public async Task Anchored_attempt_produces_lower_posterior_than_unanchored_attempt()
    {
        // Two students with identical priors and identical correct attempts.
        // One is "reference-anchored" (just saw a worked example, 60s ago);
        // the other isn't. The anchored student's posterior MUST be lower
        // because their observation counts at half weight.
        var t = DateTimeOffset.Parse("2026-04-20T09:00:00Z");
        var skill = SkillCode.Parse("math.algebra.quadratic");
        var target = ExamTargetCode.Parse("bagrut-math-5yu");

        var unanchoredStore = new InMemorySkillKeyedMasteryStore();
        var anchoredStore = new InMemorySkillKeyedMasteryStore();
        var unanchored = new BktStateTracker(unanchoredStore, new DefaultBktParameterProvider());
        var anchored = new BktStateTracker(anchoredStore, new DefaultBktParameterProvider());

        var pUn = await unanchored.UpdateAsync("stu-A", target, skill,
            isCorrect: true, occurredAt: t);
        var pAn = await anchored.UpdateAsync("stu-B", target, skill,
            isCorrect: true, occurredAt: t,
            referenceAnchoredWithinSeconds: 60);

        Assert.True(pAn < pUn,
            $"anchored posterior {pAn} should be lower than unanchored {pUn} " +
            $"(Sweller worked-example transient: half-weighted observation).");
    }

    [Fact]
    public async Task Attempt_outside_window_uses_full_weight()
    {
        // ReferenceAnchoredWithinSeconds=600 (10 min) is outside the 5-min
        // window — should behave identically to passing null.
        var t = DateTimeOffset.Parse("2026-04-20T09:00:00Z");
        var skill = SkillCode.Parse("math.algebra.quadratic");
        var target = ExamTargetCode.Parse("bagrut-math-5yu");

        var fullStore = new InMemorySkillKeyedMasteryStore();
        var lateStore = new InMemorySkillKeyedMasteryStore();
        var fullTracker = new BktStateTracker(fullStore, new DefaultBktParameterProvider());
        var lateTracker = new BktStateTracker(lateStore, new DefaultBktParameterProvider());

        var pFull = await fullTracker.UpdateAsync("stu-A", target, skill,
            true, t);
        var pLate = await lateTracker.UpdateAsync("stu-B", target, skill,
            true, t,
            referenceAnchoredWithinSeconds: 600);

        Assert.Equal(pFull, pLate);
    }

    [Fact]
    public async Task Existing_callers_that_omit_timing_observe_no_behavior_change()
    {
        // Regression guard: every existing call site (MockExamBktPropagator,
        // PerTargetDiagnosticEngine, etc.) calls UpdateAsync without the
        // timing param. Their posterior MUST be identical to the legacy
        // (pre-PRR-261) result. This tests both the default param shape
        // AND the bit-identical property at factor=1.
        var t = DateTimeOffset.Parse("2026-04-20T09:00:00Z");
        var skill = SkillCode.Parse("math.algebra.quadratic");
        var target = ExamTargetCode.Parse("bagrut-math-5yu");

        var store = new InMemorySkillKeyedMasteryStore();
        var tracker = new BktStateTracker(store, new DefaultBktParameterProvider());

        var posterior = await tracker.UpdateAsync("stu-A", target, skill,
            isCorrect: true, occurredAt: t);

        // Hand-compute what the pre-PRR-261 path would have produced.
        var p = BktParameters.Default;
        var legacy = BktTracer.Update(BktStateTracker.InitialPL, true, p);

        Assert.Equal(legacy, posterior);
    }

    [Fact]
    public async Task Audit_fields_land_on_emitted_event_for_anchored_attempt()
    {
        var sink = new CollectingSink();
        var tracker = new BktStateTracker(
            new InMemorySkillKeyedMasteryStore(),
            new DefaultBktParameterProvider(),
            sink);

        await tracker.UpdateAsync(
            "stu-1",
            ExamTargetCode.Parse("bagrut-math-5yu"),
            SkillCode.Parse("math.algebra.quadratic"),
            isCorrect: true,
            occurredAt: DateTimeOffset.Parse("2026-04-20T09:00:00Z"),
            referenceAnchoredWithinSeconds: 90);

        var ev = Assert.Single(sink.Events);
        Assert.Equal(0.5f, ev.BktDiscountFactor);
        Assert.Equal(90, ev.ReferenceAnchoredWithinSeconds);
    }

    [Fact]
    public async Task Audit_fields_show_unanchored_factor_when_no_timing_supplied()
    {
        // Caller omits timing → factor=1.0, seconds=null on the event.
        // Lets calibration analyses distinguish "we know it wasn't anchored"
        // from "the call site doesn't track timing yet".
        var sink = new CollectingSink();
        var tracker = new BktStateTracker(
            new InMemorySkillKeyedMasteryStore(),
            new DefaultBktParameterProvider(),
            sink);

        await tracker.UpdateAsync(
            "stu-1",
            ExamTargetCode.Parse("bagrut-math-5yu"),
            SkillCode.Parse("math.algebra.quadratic"),
            true, DateTimeOffset.UtcNow);

        var ev = Assert.Single(sink.Events);
        Assert.Equal(1.0f, ev.BktDiscountFactor);
        Assert.Null(ev.ReferenceAnchoredWithinSeconds);
    }

    [Fact]
    public async Task Anchored_attempt_still_advances_attempt_count_and_row()
    {
        // Spacing-benefit (ADR-0050 Item 4) is about ATTEMPT timing, not
        // about whether the attempt lands in the row. Even half-weighted
        // observations must still increment AttemptCount + UpdatedAt so
        // the scheduler's spacing arithmetic stays accurate.
        var store = new InMemorySkillKeyedMasteryStore();
        var tracker = new BktStateTracker(store, new DefaultBktParameterProvider());
        var target = ExamTargetCode.Parse("bagrut-math-5yu");
        var skill = SkillCode.Parse("math.algebra.quadratic");
        var t = DateTimeOffset.Parse("2026-04-20T09:00:00Z");

        await tracker.UpdateAsync("stu-A", target, skill, true, t,
            referenceAnchoredWithinSeconds: 60);

        var row = await store.TryGetAsync(new MasteryKey("stu-A", target, skill));
        Assert.NotNull(row);
        Assert.Equal(1, row!.AttemptCount);
        Assert.Equal(t, row.UpdatedAt);
    }

    [Fact]
    public async Task Custom_discount_policy_can_be_swapped_in()
    {
        // Sensitivity-analysis use case: substitute a stub policy and
        // verify the tracker honours its decision.
        var sink = new CollectingSink();
        var stubPolicy = new StubDiscountPolicy(returnFactor: 0.25f);
        var tracker = new BktStateTracker(
            new InMemorySkillKeyedMasteryStore(),
            new DefaultBktParameterProvider(),
            sink,
            stubPolicy);

        await tracker.UpdateAsync(
            "stu-1",
            ExamTargetCode.Parse("bagrut-math-5yu"),
            SkillCode.Parse("math.algebra.quadratic"),
            true, DateTimeOffset.UtcNow,
            referenceAnchoredWithinSeconds: 30);

        var ev = Assert.Single(sink.Events);
        Assert.Equal(0.25f, ev.BktDiscountFactor);
    }

    private sealed class StubDiscountPolicy : IBktReferenceAnchorDiscountPolicy
    {
        private readonly float _factor;
        public StubDiscountPolicy(float returnFactor) => _factor = returnFactor;
        public float ResolveDiscountFactor(int? referenceAnchoredWithinSeconds) => _factor;
    }
}
