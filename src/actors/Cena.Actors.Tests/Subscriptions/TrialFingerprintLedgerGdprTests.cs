// =============================================================================
// Cena Platform — TrialFingerprintLedger GDPR retention tests (Phase 1B)
//
// Locks in the carve-out from docs/design/trial-recycle-defense-001-research.md
// §1.5a + companion brief §5.12: when the parent's subscription stream is
// crypto-shredded under RTBF, the fingerprint ledger row MUST survive
// (otherwise "delete account, re-register" becomes a trial-recycle bypass),
// but ParentSubjectIdEncrypted and NormalizedEmail MUST be cleared because
// they are PII bound to the parent.
//
// What we assert:
//   1. ClearParentReferenceAsync nulls ParentSubjectIdEncrypted + NormalizedEmail
//   2. The row itself remains, with fingerprintHash + status + recordedAt intact
//   3. ParentReferenceCleared = true, ParentReferenceClearedAt is set
//   4. Subsequent LookupAsync still returns the row (so re-trial via the
//      same physical card still gets blocked by L3a)
//   5. Subsequent LookupByNormalizedEmailAsync no longer matches the
//      original email (the email signal is gone — that is intentional;
//      the fingerprint is the surviving signal)
//   6. The cleared row is still TrialUsed — so a subsequent
//      RecordTrialAsync with the same fingerprint STILL throws
//      trial_already_used (the abuse loop is closed)
//   7. Per-parent audit events are scrubbed — the audit stream lives with
//      the parent's subscription stream, which is shredded by ADR-0038.
//      Only the surviving doc carries the fraud-prevention record.
//   8. ClearParentReferenceAsync on a parent with no rows returns 0 (no
//      throw — RTBF on a parent that never recorded a trial is a normal
//      no-op outcome).
// =============================================================================

using Cena.Actors.Subscriptions;
using Xunit;

namespace Cena.Actors.Tests.Subscriptions;

public class TrialFingerprintLedgerGdprTests
{
    private const string Fp = "fp-hash-gdpr-test-01";
    private const string Parent = "enc::parent::rtbf-target";
    private const string OtherParent = "enc::parent::other";
    private const string Email = "victim@gmail.com";
    private const string OtherEmail = "other@gmail.com";

    [Fact]
    public async Task RTBF_preserves_row_but_clears_parent_id_and_email()
    {
        var store = new InMemoryTrialFingerprintLedgerStore();
        await store.RecordTrialAsync(Fp, Parent, Email, CancellationToken.None);

        var clearedCount = await store.ClearParentReferenceAsync(Parent, CancellationToken.None);
        Assert.Equal(1, clearedCount);

        var row = await store.LookupAsync(Fp, CancellationToken.None);
        Assert.NotNull(row);
        // Fingerprint identity preserved — fraud-prevention record stays.
        Assert.Equal(Fp, row!.FingerprintHash);
        Assert.Equal(TrialFingerprintLedgerStatus.TrialUsed, row.Status);
        Assert.NotEqual(default, row.RecordedAt);
        // PII cleared.
        Assert.Null(row.ParentSubjectIdEncrypted);
        Assert.Null(row.NormalizedEmail);
        // Audit flags set.
        Assert.True(row.ParentReferenceCleared);
        Assert.NotNull(row.ParentReferenceClearedAt);
    }

    [Fact]
    public async Task RTBF_audit_stream_for_parent_is_scrubbed()
    {
        var store = new InMemoryTrialFingerprintLedgerStore();
        await store.RecordTrialAsync(Fp, Parent, Email, CancellationToken.None);
        Assert.Single(store.ReadAuditEventsForParent(Parent));

        await store.ClearParentReferenceAsync(Parent, CancellationToken.None);

        Assert.Empty(store.ReadAuditEventsForParent(Parent));
    }

    [Fact]
    public async Task RTBF_does_not_touch_unrelated_rows()
    {
        var store = new InMemoryTrialFingerprintLedgerStore();
        await store.RecordTrialAsync(Fp, Parent, Email, CancellationToken.None);
        await store.RecordTrialAsync(
            "fp-hash-other", OtherParent, OtherEmail, CancellationToken.None);

        await store.ClearParentReferenceAsync(Parent, CancellationToken.None);

        // The other parent's row is untouched.
        var otherRow = await store.LookupAsync("fp-hash-other", CancellationToken.None);
        Assert.NotNull(otherRow);
        Assert.Equal(OtherParent, otherRow!.ParentSubjectIdEncrypted);
        Assert.Equal(OtherEmail, otherRow.NormalizedEmail);
        Assert.False(otherRow.ParentReferenceCleared);
    }

    [Fact]
    public async Task After_RTBF_recycling_same_card_is_still_blocked()
    {
        var store = new InMemoryTrialFingerprintLedgerStore();
        await store.RecordTrialAsync(Fp, Parent, Email, CancellationToken.None);
        await store.ClearParentReferenceAsync(Parent, CancellationToken.None);

        // Attacker re-registers a new account with a new email and tries
        // to start a trial with the SAME physical card. The fingerprint
        // signal must still fire.
        var ex = await Assert.ThrowsAsync<TrialAbuseException>(() =>
            store.RecordTrialAsync(
                Fp, "enc::parent::new-account-after-rtbf",
                "freshalias@gmail.com",
                CancellationToken.None));

        Assert.Equal("trial_already_used", ex.ReasonCode);
        Assert.Equal(TrialAbuseSignal.FingerprintHashMatch, ex.MatchedSignal);
    }

    [Fact]
    public async Task After_RTBF_lookup_by_old_email_returns_empty()
    {
        var store = new InMemoryTrialFingerprintLedgerStore();
        await store.RecordTrialAsync(Fp, Parent, Email, CancellationToken.None);
        await store.ClearParentReferenceAsync(Parent, CancellationToken.None);

        // Email was scrubbed; the row no longer matches by email lookup.
        // (The email signal is gone post-RTBF — fingerprint is the only
        // surviving channel for the fraud-prevention record.)
        var byEmail = await store.LookupByNormalizedEmailAsync(Email, CancellationToken.None);
        Assert.Empty(byEmail);
    }

    [Fact]
    public async Task After_RTBF_recycling_via_email_alias_is_NOT_blocked_by_email_signal()
    {
        // Subtle correctness check: post-RTBF, the email is null on the
        // surviving row. A new attempt with the SAME email but a DIFFERENT
        // fingerprint must NOT throw NormalizedEmailMatch — because the
        // PII has been scrubbed and we cannot legally retain the email-to-
        // fingerprint binding. The L3a fingerprint signal alone is the
        // surviving record. (Documenting this so future readers understand
        // the post-RTBF defense surface is L3a-only, not L2+L3a.)
        var store = new InMemoryTrialFingerprintLedgerStore();
        await store.RecordTrialAsync(Fp, Parent, Email, CancellationToken.None);
        await store.ClearParentReferenceAsync(Parent, CancellationToken.None);

        await store.RecordTrialAsync(
            "fp-hash-different-card", "enc::parent::new", Email, CancellationToken.None);

        // No throw expected. Verify the new row was recorded.
        var newRow = await store.LookupAsync("fp-hash-different-card", CancellationToken.None);
        Assert.NotNull(newRow);
        Assert.Equal(Email, newRow!.NormalizedEmail);
    }

    [Fact]
    public async Task RTBF_is_idempotent_no_throw_no_double_clear()
    {
        var store = new InMemoryTrialFingerprintLedgerStore();
        await store.RecordTrialAsync(Fp, Parent, Email, CancellationToken.None);

        var firstCount = await store.ClearParentReferenceAsync(Parent, CancellationToken.None);
        Assert.Equal(1, firstCount);

        // Second call sees no rows still pointing at Parent (we already
        // nulled them) and reports zero. Not an error — RTBF should be
        // safe to retry.
        var secondCount = await store.ClearParentReferenceAsync(Parent, CancellationToken.None);
        Assert.Equal(0, secondCount);
    }

    [Fact]
    public async Task RTBF_unknown_parent_returns_zero()
    {
        var store = new InMemoryTrialFingerprintLedgerStore();

        var count = await store.ClearParentReferenceAsync(
            "enc::parent::never-trialled", CancellationToken.None);

        Assert.Equal(0, count);
    }

    [Fact]
    public async Task RTBF_blank_parent_throws()
    {
        var store = new InMemoryTrialFingerprintLedgerStore();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.ClearParentReferenceAsync("", CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.ClearParentReferenceAsync("   ", CancellationToken.None));
    }
}
