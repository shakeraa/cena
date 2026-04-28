// =============================================================================
// Cena Platform — EmailNormalizer reuse regression test (Phase 1B)
//
// Locks in the contract that the trial fingerprint ledger consumes the
// SAME canonical form produced by EmailNormalizer.Normalize. The Phase 1A
// (state machine) and Phase 1B (fingerprint ledger) tasks share this
// normalizer; if a future refactor accidentally introduces a divergent
// normalization path, this test fires.
//
// Why this matters operationally: the L2 defense layer (email-alias
// stripping) is only as good as the symmetry between the issuer side
// (storing) and the consumer side (lookup). If the trial-start endpoint
// normalizes "Alice+study@Gmail.com" to "alice@gmail.com" but the
// duplicate-rejection lookup uses a different canonicaliser, the recycle-
// attack signal silently leaks. This test pins the invariant.
//
// We do NOT re-test EmailNormalizer's full table here — that is owned by
// EmailNormalizerTests. We test that the LEDGER STORE, when called with
// the OUTPUT of EmailNormalizer.Normalize, treats two-emails-that-fold-
// to-the-same-canonical-form as the same email.
// =============================================================================

using Cena.Actors.Subscriptions;
using Xunit;

namespace Cena.Actors.Tests.Subscriptions;

public class EmailNormalizerReuseInLedgerTests
{
    private const string Fp1 = "fp-hash-A";
    private const string Fp2 = "fp-hash-B";
    private const string Parent1 = "enc::parent::01";
    private const string Parent2 = "enc::parent::02";

    [Fact]
    public async Task Gmail_alias_variants_collide_when_normalized_through_EmailNormalizer()
    {
        var store = new InMemoryTrialFingerprintLedgerStore();

        var firstNormalized = EmailNormalizer.Normalize("Alice+study@Gmail.com");
        var secondNormalized = EmailNormalizer.Normalize("a.l.i.c.e@googlemail.com");

        // Sanity: normalizer canonicalises both to the same Gmail-folded form.
        Assert.Equal("alice@gmail.com", firstNormalized);
        Assert.Equal("alice@gmail.com", secondNormalized);

        // Record the first under fp1.
        await store.RecordTrialAsync(Fp1, Parent1, firstNormalized, CancellationToken.None);

        // The second attempt — different physical card (different fingerprint),
        // different parent, but the SAME canonical email — must collide.
        var ex = await Assert.ThrowsAsync<TrialAbuseException>(() =>
            store.RecordTrialAsync(Fp2, Parent2, secondNormalized, CancellationToken.None));

        Assert.Equal("trial_already_used", ex.ReasonCode);
        Assert.Equal(TrialAbuseSignal.NormalizedEmailMatch, ex.MatchedSignal);
    }

    [Fact]
    public async Task Lookup_by_normalized_email_works_with_normalizer_output()
    {
        var store = new InMemoryTrialFingerprintLedgerStore();
        var normalized = EmailNormalizer.Normalize("Bob.Smith+alpha@Gmail.com");
        Assert.Equal("bobsmith@gmail.com", normalized);

        await store.RecordTrialAsync(Fp1, Parent1, normalized, CancellationToken.None);

        // Caller passes a different (but equivalently-folding) variant
        // through the normalizer; lookup must hit.
        var lookup = EmailNormalizer.Normalize("BOB.SMITH+other@googlemail.com");
        Assert.Equal("bobsmith@gmail.com", lookup);

        var rows = await store.LookupByNormalizedEmailAsync(lookup, CancellationToken.None);
        Assert.Single(rows);
        Assert.Equal(Fp1, rows[0].FingerprintHash);
    }

    [Fact]
    public async Task Non_gmail_emails_are_not_folded_and_remain_distinct()
    {
        // Regression guard against accidentally over-eager normalization.
        // Yahoo / corporate mail systems treat dots as significant, so
        // "alice.smith@yahoo.com" and "alicesmith@yahoo.com" are DIFFERENT
        // mailboxes — the ledger must treat them as distinct.
        var store = new InMemoryTrialFingerprintLedgerStore();

        var dotted = EmailNormalizer.Normalize("alice.smith@yahoo.com");
        var undotted = EmailNormalizer.Normalize("alicesmith@yahoo.com");
        Assert.Equal("alice.smith@yahoo.com", dotted);
        Assert.Equal("alicesmith@yahoo.com", undotted);
        Assert.NotEqual(dotted, undotted);

        await store.RecordTrialAsync(Fp1, Parent1, dotted, CancellationToken.None);

        // Different normalized form ⇒ no collision.
        await store.RecordTrialAsync(Fp2, Parent2, undotted, CancellationToken.None);

        Assert.NotNull(await store.LookupAsync(Fp1, CancellationToken.None));
        Assert.NotNull(await store.LookupAsync(Fp2, CancellationToken.None));
    }

    [Fact]
    public void EmailNormalizer_Normalize_is_idempotent()
    {
        // Double-normalize regression guard. If a code path calls Normalize
        // twice, the second call must not change the result. (The ledger
        // does NOT renormalize, but we lock the invariant in case future
        // call sites use Normalize as a defensive idempotent step.)
        var raw = "Alice+study@GMAIL.com";
        var once = EmailNormalizer.Normalize(raw);
        var twice = EmailNormalizer.Normalize(once);
        Assert.Equal(once, twice);
    }
}
