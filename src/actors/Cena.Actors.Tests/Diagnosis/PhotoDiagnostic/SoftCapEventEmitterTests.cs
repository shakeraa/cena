// =============================================================================
// Cena Platform — SoftCapEventEmitterTests (PRR-401, EPIC-PRR-I, EPIC-PRR-J)
//
// Locks the production emitter's contract end-to-end using the in-memory
// ledger + a fake subscription aggregate store that counts Append calls.
// Covers the six invariant-defining cases called out in the task body:
//   - first call for (student, cap, month) → append exactly once
//   - second+ call for same tuple → no further append
//   - different students in same month → each appends once
//   - different cap types for same student → each appends once
//   - different months for same student+cap → each appends once
//   - NullSoftCapEventEmitter never appends (by construction)
//
// The emitter's event payload shape is also asserted — wrong field
// mapping here would silently break the AbuseDetectionWorker
// downstream consumer.
// =============================================================================

using Cena.Actors.Diagnosis.PhotoDiagnostic;
using Cena.Actors.Subscriptions;
using Cena.Actors.Subscriptions.Events;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Cena.Actors.Tests.Diagnosis.PhotoDiagnostic;

public class SoftCapEventEmitterTests
{
    private const string Photo = EntitlementSoftCapReached_V1.CapTypes.PhotoDiagnosticMonthly;
    private const string Sonnet = EntitlementSoftCapReached_V1.CapTypes.SonnetEscalationsWeekly;

    private static readonly DateTimeOffset Now =
        new(2026, 4, 23, 10, 0, 0, TimeSpan.Zero);

    private static SoftCapEventEmitter Build(
        out InMemorySubscriptionAggregateStore store,
        out InMemorySoftCapEmissionLedger ledger)
    {
        store = new InMemorySubscriptionAggregateStore();
        ledger = new InMemorySoftCapEmissionLedger();
        return new SoftCapEventEmitter(
            ledger,
            store,
            NullLogger<SoftCapEventEmitter>.Instance);
    }

    private static async Task<int> SoftCapEventsOn(
        InMemorySubscriptionAggregateStore store,
        string parentSubjectIdEncrypted)
    {
        var events = await store.ReadEventsAsync(parentSubjectIdEncrypted, default);
        return events.OfType<EntitlementSoftCapReached_V1>().Count();
    }

    [Fact]
    public async Task First_call_appends_EntitlementSoftCapReached_V1_with_expected_payload()
    {
        var emitter = Build(out var store, out _);

        await emitter.EmitIfFirstInPeriodAsync(
            studentSubjectIdHash: "stu-a-hash",
            parentSubjectIdEncrypted: "parent-enc-1",
            capType: Photo,
            usageCount: 101,
            capLimit: 100,
            nowUtc: Now,
            ct: default);

        var events = await store.ReadEventsAsync("parent-enc-1", default);
        var soft = Assert.Single(events.OfType<EntitlementSoftCapReached_V1>());
        Assert.Equal("parent-enc-1", soft.ParentSubjectIdEncrypted);
        Assert.Equal("stu-a-hash", soft.StudentSubjectIdEncrypted);
        Assert.Equal(Photo, soft.CapType);
        Assert.Equal(101, soft.UsageCount);
        Assert.Equal(100, soft.CapLimit);
        Assert.Equal(Now, soft.ReachedAt);
    }

    [Fact]
    public async Task Second_call_for_same_student_cap_and_month_does_not_append_again()
    {
        // The bug-magnet case: every upload past 101 re-enters the hot
        // path. The emitter MUST be a silent no-op on the second call.
        var emitter = Build(out var store, out _);

        await emitter.EmitIfFirstInPeriodAsync(
            "stu-a-hash", "parent-enc-1", Photo, 101, 100, Now, default);
        await emitter.EmitIfFirstInPeriodAsync(
            "stu-a-hash", "parent-enc-1", Photo, 150, 100, Now.AddHours(3), default);
        await emitter.EmitIfFirstInPeriodAsync(
            "stu-a-hash", "parent-enc-1", Photo, 180, 100, Now.AddDays(2), default);

        Assert.Equal(1, await SoftCapEventsOn(store, "parent-enc-1"));
    }

    [Fact]
    public async Task Different_students_in_same_month_each_produce_their_own_event()
    {
        // Two students on the same subscription family each hit the cap
        // independently — both emissions must land. Stream key is the
        // parent id; both events end up on the parent's stream, so the
        // count on that stream is 2.
        var emitter = Build(out var store, out _);

        await emitter.EmitIfFirstInPeriodAsync(
            "stu-a-hash", "parent-enc-1", Photo, 101, 100, Now, default);
        await emitter.EmitIfFirstInPeriodAsync(
            "stu-b-hash", "parent-enc-1", Photo, 101, 100, Now, default);

        Assert.Equal(2, await SoftCapEventsOn(store, "parent-enc-1"));
    }

    [Fact]
    public async Task Different_cap_types_for_same_student_each_produce_an_event()
    {
        // A student that trips photo cap AND sonnet cap in the same
        // month must produce two distinct EntitlementSoftCapReached
        // rows (CapType = photo_diagnostic_monthly and
        // sonnet_escalations_weekly respectively).
        var emitter = Build(out var store, out _);

        await emitter.EmitIfFirstInPeriodAsync(
            "stu-a-hash", "parent-enc-1", Photo, 101, 100, Now, default);
        await emitter.EmitIfFirstInPeriodAsync(
            "stu-a-hash", "parent-enc-1", Sonnet, 21, 20, Now, default);

        var events = (await store.ReadEventsAsync("parent-enc-1", default))
            .OfType<EntitlementSoftCapReached_V1>()
            .ToList();
        Assert.Equal(2, events.Count);
        Assert.Contains(events, e => e.CapType == Photo);
        Assert.Contains(events, e => e.CapType == Sonnet);
    }

    [Fact]
    public async Task Different_months_for_same_student_and_cap_each_produce_an_event()
    {
        // Monthly reset invariant: April event + May event both land.
        var emitter = Build(out var store, out _);

        await emitter.EmitIfFirstInPeriodAsync(
            "stu-a-hash", "parent-enc-1", Photo, 101, 100, Now, default);
        await emitter.EmitIfFirstInPeriodAsync(
            "stu-a-hash", "parent-enc-1", Photo, 103, 100,
            new DateTimeOffset(2026, 5, 3, 11, 0, 0, TimeSpan.Zero),
            default);

        Assert.Equal(2, await SoftCapEventsOn(store, "parent-enc-1"));
    }

    [Fact]
    public async Task Concurrent_emissions_for_same_tuple_still_only_produce_one_event()
    {
        // Sixteen parallel calls racing the same tuple — the ledger's
        // atomic claim guarantees exactly one append.
        var emitter = Build(out var store, out _);

        var tasks = Enumerable.Range(0, 16)
            .Select(_ => emitter.EmitIfFirstInPeriodAsync(
                "stu-a-hash", "parent-enc-1", Photo, 101, 100, Now, default))
            .ToArray();
        await Task.WhenAll(tasks);

        Assert.Equal(1, await SoftCapEventsOn(store, "parent-enc-1"));
    }

    [Fact]
    public async Task Missing_student_hash_throws()
    {
        var emitter = Build(out _, out _);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            emitter.EmitIfFirstInPeriodAsync(
                "", "parent-enc-1", Photo, 101, 100, Now, default));
    }

    [Fact]
    public async Task Missing_parent_id_throws()
    {
        var emitter = Build(out _, out _);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            emitter.EmitIfFirstInPeriodAsync(
                "stu-a-hash", "", Photo, 101, 100, Now, default));
    }

    [Fact]
    public async Task Null_emitter_never_appends()
    {
        // NullSoftCapEventEmitter is the no-subscription-store fallback.
        // Calling it N times must leave the subscription store untouched.
        var emitter = new NullSoftCapEventEmitter(
            NullLogger<NullSoftCapEventEmitter>.Instance);
        var store = new InMemorySubscriptionAggregateStore();

        for (var i = 0; i < 5; i++)
        {
            await emitter.EmitIfFirstInPeriodAsync(
                "stu-a-hash", "parent-enc-1", Photo, 100 + i, 100, Now, default);
        }

        var events = await store.ReadEventsAsync("parent-enc-1", default);
        Assert.Empty(events);
    }
}
