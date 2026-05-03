// =============================================================================
// Cena Platform — SoftCapEmissionLedgerTests (PRR-401, EPIC-PRR-I, EPIC-PRR-J)
//
// Locks the ledger contract independently of the emitter: once-per-tuple
// TryClaim semantics, per-student independence, per-cap-type independence,
// per-month reset, and HasEmitted probe behaviour. The tests are scoped
// at the port; both InMemory and (later) Marten implementations must
// pass the same shape.
// =============================================================================

using Cena.Actors.Diagnosis.PhotoDiagnostic;
using Cena.Actors.Subscriptions.Events;
using Xunit;

namespace Cena.Actors.Tests.Diagnosis.PhotoDiagnostic;

public class SoftCapEmissionLedgerTests
{
    private const string Photo = EntitlementSoftCapReached_V1.CapTypes.PhotoDiagnosticMonthly;
    private const string Sonnet = EntitlementSoftCapReached_V1.CapTypes.SonnetEscalationsWeekly;

    private static readonly DateTimeOffset Now =
        new(2026, 4, 23, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task TryClaim_first_call_returns_true_and_second_call_returns_false()
    {
        var ledger = new InMemorySoftCapEmissionLedger();

        var first = await ledger.TryClaimAsync("stu-a-hash", Photo, "2026-04", Now, default);
        var second = await ledger.TryClaimAsync("stu-a-hash", Photo, "2026-04", Now.AddMinutes(5), default);

        Assert.True(first);
        Assert.False(second);
    }

    [Fact]
    public async Task TryClaim_independent_per_student_same_cap_and_month()
    {
        // Two students both hitting photo soft cap the same month must
        // both claim true — their tuples differ on StudentSubjectIdHash.
        var ledger = new InMemorySoftCapEmissionLedger();

        var a = await ledger.TryClaimAsync("stu-a-hash", Photo, "2026-04", Now, default);
        var b = await ledger.TryClaimAsync("stu-b-hash", Photo, "2026-04", Now, default);

        Assert.True(a);
        Assert.True(b);
    }

    [Fact]
    public async Task TryClaim_independent_per_cap_type_same_student_and_month()
    {
        // A single student hitting two different caps the same month is
        // two distinct telemetry events — both claim true.
        var ledger = new InMemorySoftCapEmissionLedger();

        var photoClaim = await ledger.TryClaimAsync("stu-a-hash", Photo, "2026-04", Now, default);
        var sonnetClaim = await ledger.TryClaimAsync("stu-a-hash", Sonnet, "2026-04", Now, default);

        Assert.True(photoClaim);
        Assert.True(sonnetClaim);
    }

    [Fact]
    public async Task TryClaim_resets_per_month_for_same_student_and_cap()
    {
        // April + May are separate (student, cap, month) tuples. The May
        // emission must NOT be suppressed by the April row.
        var ledger = new InMemorySoftCapEmissionLedger();

        var april = await ledger.TryClaimAsync("stu-a-hash", Photo, "2026-04", Now, default);
        var may = await ledger.TryClaimAsync("stu-a-hash", Photo, "2026-05",
            Now.AddMonths(1), default);

        Assert.True(april);
        Assert.True(may);
    }

    [Fact]
    public async Task HasEmitted_returns_false_before_claim()
    {
        var ledger = new InMemorySoftCapEmissionLedger();
        Assert.False(await ledger.HasEmittedAsync("stu-a-hash", Photo, "2026-04", default));
    }

    [Fact]
    public async Task HasEmitted_returns_true_after_claim()
    {
        var ledger = new InMemorySoftCapEmissionLedger();
        await ledger.TryClaimAsync("stu-a-hash", Photo, "2026-04", Now, default);

        Assert.True(await ledger.HasEmittedAsync("stu-a-hash", Photo, "2026-04", default));
        // Other tuples that differ in any component remain unemitted.
        Assert.False(await ledger.HasEmittedAsync("stu-b-hash", Photo, "2026-04", default));
        Assert.False(await ledger.HasEmittedAsync("stu-a-hash", Sonnet, "2026-04", default));
        Assert.False(await ledger.HasEmittedAsync("stu-a-hash", Photo, "2026-05", default));
    }

    [Fact]
    public void KeyOf_rejects_blank_components()
    {
        Assert.Throws<ArgumentException>(() =>
            SoftCapEmissionLedgerDocument.KeyOf("", Photo, "2026-04"));
        Assert.Throws<ArgumentException>(() =>
            SoftCapEmissionLedgerDocument.KeyOf("stu-a-hash", "", "2026-04"));
        Assert.Throws<ArgumentException>(() =>
            SoftCapEmissionLedgerDocument.KeyOf("stu-a-hash", Photo, ""));
    }

    [Fact]
    public void KeyOf_builds_expected_compound_id_shape()
    {
        var id = SoftCapEmissionLedgerDocument.KeyOf("stu-a-hash", Photo, "2026-04");
        Assert.Equal("stu-a-hash|photo_diagnostic_monthly|2026-04", id);
    }

    [Fact]
    public async Task TryClaim_is_safe_under_concurrent_duplicate_writers()
    {
        // Sixteen concurrent attempts for the same tuple must observe
        // exactly ONE true. This is the atomicity invariant that keeps
        // the emitter from appending duplicate EntitlementSoftCapReached
        // events to the parent's subscription stream under load.
        var ledger = new InMemorySoftCapEmissionLedger();
        var tasks = Enumerable.Range(0, 16)
            .Select(_ => ledger.TryClaimAsync("stu-a-hash", Photo, "2026-04", Now, default))
            .ToArray();
        var results = await Task.WhenAll(tasks);
        Assert.Equal(1, results.Count(r => r));
    }
}
