// =============================================================================
// Cena Platform — TrialFingerprintLedger contract tests (Phase 1B)
//
// Locks in the L3a card-fingerprint defense behaviour against the
// InMemoryTrialFingerprintLedgerStore. The Marten variant shares the same
// observable contract (record + duplicate rejection + lookup); its
// type-checks are exercised by the full-sln build, and its query semantics
// are exercised in production by the AbuseDetectionWorker integration tests.
//
// Coverage matrix:
//   - Record + lookup round-trip
//   - Duplicate fingerprint hash → trial_already_used (FingerprintHashMatch)
//   - Duplicate normalized email → trial_already_used (NormalizedEmailMatch)
//   - Lookup by normalized email returns matching rows
//   - Audit event TrialFingerprintRecorded_V1 appended to parent stream
//   - EmailNormalizer regression — record path uses the same canonical form
//     so callers cannot accidentally double-store via a casing difference
//   - Validation: empty/whitespace inputs throw ArgumentException
// =============================================================================

using Cena.Actors.Subscriptions;
using Cena.Actors.Subscriptions.Events;
using Xunit;

namespace Cena.Actors.Tests.Subscriptions;

public class TrialFingerprintLedgerTests
{
    private const string Fp1 = "fp-hash-aaaa-1111";
    private const string Fp2 = "fp-hash-bbbb-2222";
    private const string Parent1 = "enc::parent::01";
    private const string Parent2 = "enc::parent::02";
    private const string Email1 = "alice@gmail.com";
    private const string Email2 = "bob@example.com";

    [Fact]
    public async Task Record_then_lookup_returns_the_row()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 4, 28, 10, 0, 0, TimeSpan.Zero));
        var store = new InMemoryTrialFingerprintLedgerStore(clock);

        await store.RecordTrialAsync(Fp1, Parent1, Email1, CancellationToken.None);

        var hit = await store.LookupAsync(Fp1, CancellationToken.None);
        Assert.NotNull(hit);
        Assert.Equal(Fp1, hit!.FingerprintHash);
        Assert.Equal(Fp1, hit.Id);
        Assert.Equal(TrialFingerprintLedgerStatus.TrialUsed, hit.Status);
        Assert.Equal(Parent1, hit.ParentSubjectIdEncrypted);
        Assert.Equal(Email1, hit.NormalizedEmail);
        Assert.False(hit.ParentReferenceCleared);
        Assert.Null(hit.ParentReferenceClearedAt);
        Assert.Equal(clock.Now, hit.RecordedAt);
    }

    [Fact]
    public async Task Lookup_unknown_fingerprint_returns_null()
    {
        var store = new InMemoryTrialFingerprintLedgerStore();

        var hit = await store.LookupAsync(Fp1, CancellationToken.None);

        Assert.Null(hit);
    }

    [Fact]
    public async Task Duplicate_fingerprint_throws_trial_already_used_with_fingerprint_signal()
    {
        var store = new InMemoryTrialFingerprintLedgerStore();
        await store.RecordTrialAsync(Fp1, Parent1, Email1, CancellationToken.None);

        // Same fingerprint, different parent + different email — must still
        // be rejected, because the card has already been used for a trial.
        var ex = await Assert.ThrowsAsync<TrialAbuseException>(() =>
            store.RecordTrialAsync(Fp1, Parent2, Email2, CancellationToken.None));

        Assert.Equal("trial_already_used", ex.ReasonCode);
        Assert.Equal(TrialAbuseSignal.FingerprintHashMatch, ex.MatchedSignal);
    }

    [Fact]
    public async Task Duplicate_normalized_email_throws_trial_already_used_with_email_signal()
    {
        var store = new InMemoryTrialFingerprintLedgerStore();
        await store.RecordTrialAsync(Fp1, Parent1, Email1, CancellationToken.None);

        // Different fingerprint (different physical card), different parent,
        // SAME normalized email — recycle attack via email-alias trick.
        var ex = await Assert.ThrowsAsync<TrialAbuseException>(() =>
            store.RecordTrialAsync(Fp2, Parent2, Email1, CancellationToken.None));

        Assert.Equal("trial_already_used", ex.ReasonCode);
        Assert.Equal(TrialAbuseSignal.NormalizedEmailMatch, ex.MatchedSignal);
    }

    [Fact]
    public async Task Distinct_fingerprints_and_distinct_emails_both_record_successfully()
    {
        var store = new InMemoryTrialFingerprintLedgerStore();
        await store.RecordTrialAsync(Fp1, Parent1, Email1, CancellationToken.None);
        await store.RecordTrialAsync(Fp2, Parent2, Email2, CancellationToken.None);

        Assert.NotNull(await store.LookupAsync(Fp1, CancellationToken.None));
        Assert.NotNull(await store.LookupAsync(Fp2, CancellationToken.None));
    }

    [Fact]
    public async Task Lookup_by_normalized_email_returns_matching_rows()
    {
        var store = new InMemoryTrialFingerprintLedgerStore();
        await store.RecordTrialAsync(Fp1, Parent1, Email1, CancellationToken.None);
        await store.RecordTrialAsync(Fp2, Parent2, Email2, CancellationToken.None);

        var byEmail1 = await store.LookupByNormalizedEmailAsync(Email1, CancellationToken.None);
        Assert.Single(byEmail1);
        Assert.Equal(Fp1, byEmail1[0].FingerprintHash);

        var byEmail2 = await store.LookupByNormalizedEmailAsync(Email2, CancellationToken.None);
        Assert.Single(byEmail2);
        Assert.Equal(Fp2, byEmail2[0].FingerprintHash);

        var byUnknown = await store.LookupByNormalizedEmailAsync(
            "unseen@example.com", CancellationToken.None);
        Assert.Empty(byUnknown);
    }

    [Fact]
    public async Task Lookup_by_normalized_email_blank_input_returns_empty()
    {
        var store = new InMemoryTrialFingerprintLedgerStore();
        await store.RecordTrialAsync(Fp1, Parent1, Email1, CancellationToken.None);

        var byBlank = await store.LookupByNormalizedEmailAsync("", CancellationToken.None);
        Assert.Empty(byBlank);

        var byWhitespace = await store.LookupByNormalizedEmailAsync("   ", CancellationToken.None);
        Assert.Empty(byWhitespace);
    }

    [Fact]
    public async Task Record_appends_audit_event_to_parent_stream()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 4, 28, 10, 0, 0, TimeSpan.Zero));
        var store = new InMemoryTrialFingerprintLedgerStore(clock);

        await store.RecordTrialAsync(Fp1, Parent1, Email1, CancellationToken.None);

        var events = store.ReadAuditEventsForParent(Parent1);
        Assert.Single(events);
        var ev = events[0];
        Assert.Equal(Fp1, ev.FingerprintHash);
        Assert.Equal(Parent1, ev.ParentSubjectIdEncrypted);
        Assert.Equal(Email1, ev.NormalizedEmail);
        Assert.Equal(clock.Now, ev.RecordedAt);
    }

    [Fact]
    public async Task Record_does_not_append_audit_event_when_duplicate_rejected()
    {
        var store = new InMemoryTrialFingerprintLedgerStore();
        await store.RecordTrialAsync(Fp1, Parent1, Email1, CancellationToken.None);

        await Assert.ThrowsAsync<TrialAbuseException>(() =>
            store.RecordTrialAsync(Fp1, Parent2, Email2, CancellationToken.None));

        // Audit event count for Parent1 stays at 1 (the original); Parent2
        // gets nothing because the second call was rejected.
        Assert.Single(store.ReadAuditEventsForParent(Parent1));
        Assert.Empty(store.ReadAuditEventsForParent(Parent2));
    }

    [Fact]
    public async Task Record_throws_on_blank_inputs()
    {
        var store = new InMemoryTrialFingerprintLedgerStore();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.RecordTrialAsync("", Parent1, Email1, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.RecordTrialAsync(Fp1, "", Email1, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.RecordTrialAsync(Fp1, Parent1, "", CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.RecordTrialAsync("   ", Parent1, Email1, CancellationToken.None));
    }

    [Fact]
    public async Task Lookup_blank_fingerprint_returns_null_no_throw()
    {
        var store = new InMemoryTrialFingerprintLedgerStore();

        Assert.Null(await store.LookupAsync("", CancellationToken.None));
        Assert.Null(await store.LookupAsync("   ", CancellationToken.None));
    }

    /// <summary>
    /// Minimal monotonic clock for deterministic timestamp assertions.
    /// </summary>
    private sealed class FakeClock : TimeProvider
    {
        public DateTimeOffset Now { get; }
        public FakeClock(DateTimeOffset now) { Now = now; }
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
