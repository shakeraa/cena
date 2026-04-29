// =============================================================================
// Cena Platform — Bagrut Reference Consent Flow Tests (PRR-267)
//
// End-to-end unit-level integration coverage of the ADR-0059 §15.3 token +
// consent + Reference<T>.From() round trip. No WebApplicationFactory —
// the IConsentAggregateStore InMemory implementation is the test seam, so
// these run fast (~ms each) and are deterministic.
//
// Coverage target:
//
//   1. HMAC token issue / verify round-trip (clean signature)
//   2. HMAC tamper detection (Verify returns false on any field flip)
//   3. Wire-TTL expiry honoured by both ConsentTokenId.IsExpired and Verify
//   4. Cross-context replay refused (BrowseLibrary token can't satisfy
//      VariantSourceCitation)
//   5. Cross-student replay refused (token bound to studentId at issue)
//   6. Consent stream replay produces ConsentState.BagrutReference with
//      the right grant/revoke history
//   7. Reference<T>.From() integration: token issued by service →
//      validates inside the wrapper factory → audit event would emit
//   8. Revoke-before-grant tolerance (defensive; ConsentState handles)
// =============================================================================

using Cena.Actors.Consent;
using Cena.Actors.Consent.Events;
using Cena.Actors.Content;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Cena.Actors.Tests.Consent;

public sealed class BagrutReferenceConsentFlowTests
{
    private const string Pepper = "test-pepper-deterministic-not-for-prod";
    private const string StudentId = "stu-test-1";

    // ────────────────────────────────────────────────────────────────────
    // Token service round-trip
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public void Token_round_trip_succeeds_for_same_student_same_context()
    {
        var svc = new BagrutReferenceConsentTokenService(Pepper);
        var now = DateTimeOffset.UtcNow;

        var token = svc.Issue(StudentId, ReferenceContextKind.BrowseLibrary, now);

        Assert.True(svc.Verify(token, StudentId, ReferenceContextKind.BrowseLibrary, now));
    }

    [Fact]
    public void Token_verify_fails_after_wire_ttl_elapses()
    {
        var svc = new BagrutReferenceConsentTokenService(Pepper);
        var now = DateTimeOffset.UtcNow;
        var token = svc.Issue(StudentId, ReferenceContextKind.BrowseLibrary, now);

        // Move the clock past the 24h wire TTL.
        var future = now + BagrutReferenceConsentTokenService.WireTtl + TimeSpan.FromSeconds(1);

        Assert.False(svc.Verify(token, StudentId, ReferenceContextKind.BrowseLibrary, future));
        Assert.True(token.IsExpired(future));
    }

    [Fact]
    public void Token_verify_fails_for_different_student()
    {
        var svc = new BagrutReferenceConsentTokenService(Pepper);
        var now = DateTimeOffset.UtcNow;
        var token = svc.Issue(StudentId, ReferenceContextKind.BrowseLibrary, now);

        Assert.False(svc.Verify(token, "stu-different", ReferenceContextKind.BrowseLibrary, now));
    }

    [Fact]
    public void Token_verify_fails_for_different_context()
    {
        // ADR-0059 §15.3 redteam mitigation: BrowseLibrary token MUST NOT
        // satisfy a VariantSourceCitation render.
        var svc = new BagrutReferenceConsentTokenService(Pepper);
        var now = DateTimeOffset.UtcNow;
        var browseToken = svc.Issue(StudentId, ReferenceContextKind.BrowseLibrary, now);

        Assert.False(svc.Verify(browseToken, StudentId, ReferenceContextKind.VariantSourceCitation, now));
    }

    [Fact]
    public void Token_verify_fails_when_hmac_tampered()
    {
        var svc = new BagrutReferenceConsentTokenService(Pepper);
        var now = DateTimeOffset.UtcNow;
        var token = svc.Issue(StudentId, ReferenceContextKind.BrowseLibrary, now);

        // Flip a single byte in the HMAC. Verify must reject.
        var tamperedHmac = token.TokenHmac[..^1] + (token.TokenHmac[^1] == 'A' ? 'B' : 'A');
        var tampered = token with { TokenHmac = tamperedHmac };

        Assert.False(svc.Verify(tampered, StudentId, ReferenceContextKind.BrowseLibrary, now));
    }

    [Fact]
    public void Token_verify_fails_when_pepper_differs()
    {
        var issuer = new BagrutReferenceConsentTokenService(Pepper);
        var verifier = new BagrutReferenceConsentTokenService("different-pepper");
        var now = DateTimeOffset.UtcNow;
        var token = issuer.Issue(StudentId, ReferenceContextKind.BrowseLibrary, now);

        // A second service with a different pepper recomputes a different
        // HMAC and rejects the token. Defends against pepper-rotation
        // bugs forging valid tokens.
        Assert.False(verifier.Verify(token, StudentId, ReferenceContextKind.BrowseLibrary, now));
    }

    // ────────────────────────────────────────────────────────────────────
    // Consent stream + ConsentState fold
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Consent_grant_then_revoke_replays_to_BagrutReference_revoked()
    {
        var store = new InMemoryConsentAggregateStore();
        var now = DateTimeOffset.UtcNow;

        await store.AppendAsync(StudentId, new BagrutReferenceConsentGranted_V1(
            StudentId: StudentId,
            GrantedAt: now,
            DisclosureVersion: "v1",
            UserAgent: null,
            IpAddressHash: null));
        await store.AppendAsync(StudentId, new BagrutReferenceConsentRevoked_V1(
            StudentId: StudentId,
            RevokedAt: now.AddMinutes(5),
            Reason: "user-initiated"));

        var aggregate = await store.LoadAsync(StudentId);

        Assert.NotNull(aggregate.State.BagrutReference);
        Assert.NotNull(aggregate.State.BagrutReference.RevokedAt);
        Assert.Equal("user-initiated", aggregate.State.BagrutReference.RevocationReason);
        // After revoke + within the 90d functional TTL, IsActive returns
        // false because RevokedAt is set.
        Assert.False(aggregate.State.BagrutReference.IsActive(
            now.AddMinutes(10), TimeSpan.FromDays(90)));
    }

    [Fact]
    public async Task Consent_grant_only_is_active_within_functional_ttl()
    {
        var store = new InMemoryConsentAggregateStore();
        var now = DateTimeOffset.UtcNow;

        await store.AppendAsync(StudentId, new BagrutReferenceConsentGranted_V1(
            StudentId: StudentId,
            GrantedAt: now,
            DisclosureVersion: "v1",
            UserAgent: "Mozilla/5.0",
            IpAddressHash: null));

        var aggregate = await store.LoadAsync(StudentId);
        Assert.NotNull(aggregate.State.BagrutReference);
        Assert.True(aggregate.State.BagrutReference.IsActive(
            now.AddDays(89), TimeSpan.FromDays(90)));
        Assert.False(aggregate.State.BagrutReference.IsActive(
            now.AddDays(91), TimeSpan.FromDays(90)));
    }

    [Fact]
    public async Task Revoke_before_grant_records_negative_fact()
    {
        // Defensive: pure-replay tolerance for an out-of-order revoke
        // event. The wire endpoint guards against this, but the projection
        // must not throw if Marten replays in a surprise order during
        // schema rebuilds.
        var store = new InMemoryConsentAggregateStore();
        var now = DateTimeOffset.UtcNow;

        await store.AppendAsync(StudentId, new BagrutReferenceConsentRevoked_V1(
            StudentId: StudentId,
            RevokedAt: now,
            Reason: "out-of-order-replay"));

        var aggregate = await store.LoadAsync(StudentId);
        Assert.NotNull(aggregate.State.BagrutReference);
        Assert.NotNull(aggregate.State.BagrutReference.RevokedAt);
        Assert.False(aggregate.State.BagrutReference.IsActive(now, TimeSpan.FromDays(90)));
    }

    [Fact]
    public async Task ItemRendered_event_is_pure_audit_no_state_fold()
    {
        // §15.7 invariant: rendered events are SIEM-correlation only;
        // they do NOT mutate ConsentState.BagrutReference. A grant
        // followed by 100 renders should leave BagrutReference identical
        // to the grant-only case.
        var store = new InMemoryConsentAggregateStore();
        var now = DateTimeOffset.UtcNow;

        await store.AppendAsync(StudentId, new BagrutReferenceConsentGranted_V1(
            StudentId: StudentId,
            GrantedAt: now,
            DisclosureVersion: "v1",
            UserAgent: null,
            IpAddressHash: null));

        for (var i = 0; i < 5; i++)
        {
            await store.AppendAsync(StudentId, new BagrutReferenceItemRendered_V1(
                StudentId: StudentId,
                ItemId: $"item-{i}",
                ProvenanceSource: $"ministry-bagrut/035581/2024/summer/A/q{i + 1}",
                ContextKind: nameof(ReferenceContextKind.BrowseLibrary),
                ConsentTokenIssuedAt: now.ToString("O"),
                RenderedAt: now.AddMinutes(i + 1)));
        }

        var aggregate = await store.LoadAsync(StudentId);
        Assert.NotNull(aggregate.State.BagrutReference);
        Assert.Null(aggregate.State.BagrutReference.RevokedAt);
        // Grant timestamp unchanged after 5 render events.
        Assert.Equal(now, aggregate.State.BagrutReference.GrantedAt);
    }

    // ────────────────────────────────────────────────────────────────────
    // Reference<T>.From integration with the issued token
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public void Reference_From_accepts_token_issued_by_service()
    {
        // Full integration: a token produced by the real service round-
        // trips through Reference<T>.From() without forging.
        var svc = new BagrutReferenceConsentTokenService(Pepper);
        var now = DateTimeOffset.UtcNow;
        var token = svc.Issue(StudentId, ReferenceContextKind.BrowseLibrary, now);

        var provenance = new Provenance(
            ProvenanceKind.MinistryBagrut,
            now,
            Source: "ministry-bagrut/035581/2024/summer/A/q3");

        var wrapped = Reference<string>.From(
            value: "Ministry-derived stem",
            provenance: provenance,
            consentToken: token,
            context: ReferenceContextKind.BrowseLibrary,
            auditLogger: NullLogger.Instance,
            now: now,
            itemId: "item-1");

        Assert.Equal("Ministry-derived stem", wrapped.Value);
        Assert.Equal(token, wrapped.ConsentToken);
        Assert.Equal(ProvenanceKind.MinistryBagrut, wrapped.Provenance.Kind);
    }
}
