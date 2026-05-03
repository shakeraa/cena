// =============================================================================
// Cena Platform — ConsentAggregate erasure integration tests (prr-155)
//
// Verifies the ADR-0038 crypto-shred round-trip for consent events:
//   1. Command handler encrypts subjectId + actorId on event write.
//   2. Tombstoning the subject key renders the ciphertext undecryptable.
//   3. Reading the event via EncryptedFieldAccessor.TryDecryptAsync after
//      tombstone returns ErasedSentinel.Value ("[erased]").
//
// These tests simulate the ErasureWorker flow end-to-end at the primitive
// level — we do not stand up a Marten store, but we do exercise the full
// EncryptedFieldAccessor + ISubjectKeyStore contract.
// =============================================================================

using Cena.Actors.Consent;
using Cena.Infrastructure.Compliance;
using Cena.Infrastructure.Compliance.KeyStore;

namespace Cena.Actors.Tests.Consent;

public sealed class ConsentErasureIntegrationTests
{
    private static readonly byte[] TestRootKey = new byte[]
    {
        0xC0, 0xA5, 0xEC, 0xEF, 0xEF, 0xE5, 0xEA, 0xE1,
        0xF2, 0xE5, 0xA5, 0xF1, 0xA5, 0xE5, 0xF2, 0xE9,
        0xEC, 0xE1, 0xE4, 0xE9, 0xE2, 0xE5, 0xF2, 0xA5,
        0xE1, 0xF5, 0xE5, 0xEE, 0xF2, 0xE5, 0xF2, 0xE9
    };

    private static (InMemorySubjectKeyStore keyStore, EncryptedFieldAccessor accessor, ConsentCommandHandler handler, InMemoryConsentAggregateStore store) BuildSut()
    {
        var derivation = new SubjectKeyDerivation(TestRootKey, "erasure-test", isDevFallback: false);
        var keyStore = new InMemorySubjectKeyStore(derivation);
        var accessor = new EncryptedFieldAccessor(keyStore);
        var handler = new ConsentCommandHandler(accessor);
        var store = new InMemoryConsentAggregateStore();
        return (keyStore, accessor, handler, store);
    }

    [Fact]
    public async Task ConsentGranted_SubjectId_and_ActorId_are_encrypted_on_wire()
    {
        var (_, accessor, handler, store) = BuildSut();
        const string subjectId = "student-erase-alpha";
        const string actorId = "student-erase-alpha"; // self-service

        var evt = await handler.HandleAsync(new GrantConsent(
            subjectId, AgeBand.Adult, ConsentPurpose.MisconceptionDetection,
            Scope: "institute-A", GrantedByRole: ActorRole.Student,
            GrantedByActorId: actorId, GrantedAt: DateTimeOffset.UtcNow, ExpiresAt: null));
        await store.AppendAsync(subjectId, evt);

        // Plaintext must NOT appear in the encrypted fields.
        Assert.NotEqual(subjectId, evt.SubjectIdEncrypted);
        Assert.NotEqual(actorId, evt.GrantedByActorIdEncrypted);
        Assert.StartsWith("cena.aesgcm.v1:", evt.SubjectIdEncrypted);
        Assert.StartsWith("cena.aesgcm.v1:", evt.GrantedByActorIdEncrypted);

        // Before erasure, TryDecryptAsync returns the plaintext.
        var (sSuccess, sPlain) = await accessor.TryDecryptAsync(evt.SubjectIdEncrypted, subjectId);
        Assert.True(sSuccess);
        Assert.Equal(subjectId, sPlain);

        var (aSuccess, aPlain) = await accessor.TryDecryptAsync(evt.GrantedByActorIdEncrypted, subjectId);
        Assert.True(aSuccess);
        Assert.Equal(actorId, aPlain);
    }

    [Fact]
    public async Task Tombstone_subject_key_renders_consent_event_PII_erased()
    {
        var (keyStore, accessor, handler, store) = BuildSut();
        const string subjectId = "student-erase-bravo";

        var evt = await handler.HandleAsync(new GrantConsent(
            subjectId, AgeBand.Adult, ConsentPurpose.AiAssistance,
            Scope: "tutoring", GrantedByRole: ActorRole.Student,
            GrantedByActorId: subjectId, GrantedAt: DateTimeOffset.UtcNow, ExpiresAt: null));
        await store.AppendAsync(subjectId, evt);

        // Simulate the ErasureWorker path: tombstone the subject key.
        var wasAlive = await keyStore.DeleteAsync(subjectId);
        Assert.True(wasAlive);

        // Any subsequent decrypt attempt returns ErasedSentinel.
        var (sSuccess, sPlain) = await accessor.TryDecryptAsync(evt.SubjectIdEncrypted, subjectId);
        Assert.False(sSuccess);
        Assert.Equal(ErasedSentinel.Value, sPlain);

        var (aSuccess, aPlain) = await accessor.TryDecryptAsync(evt.GrantedByActorIdEncrypted, subjectId);
        Assert.False(aSuccess);
        Assert.Equal(ErasedSentinel.Value, aPlain);

        // Event stream itself remains — ADR-0038 append-only invariant preserved.
        var agg = await store.LoadAsync(subjectId);
        Assert.True(agg.State.IsEffectivelyGranted(ConsentPurpose.AiAssistance, DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task ParentReview_event_PII_is_also_crypto_shredded()
    {
        var (keyStore, accessor, handler, store) = BuildSut();
        const string subjectId = "student-erase-charlie";
        const string parentId = "parent-delta";
        var purposes = new List<ConsentPurpose> { ConsentPurpose.MisconceptionDetection };

        var evt = await handler.HandleAsync(new RecordParentReview(
            StudentSubjectId: subjectId, StudentBand: AgeBand.Teen13to15,
            ParentActorId: parentId, PurposesReviewed: purposes,
            Outcome: ConsentReviewOutcome.Approved,
            ReviewedAt: DateTimeOffset.UtcNow));
        await store.AppendAsync(subjectId, evt);

        Assert.NotEqual(subjectId, evt.StudentSubjectIdEncrypted);
        Assert.NotEqual(parentId, evt.ParentActorIdEncrypted);

        // Tombstone the student — note: parent PII is encrypted under the
        // STUDENT's subject key (stream-scoped), so student tombstone
        // erases parent identifier from this stream too. Cross-stream
        // correlation of the parent on a PARENT-scoped stream would use
        // the parent's own subject key.
        await keyStore.DeleteAsync(subjectId);

        var (success, plain) = await accessor.TryDecryptAsync(evt.StudentSubjectIdEncrypted, subjectId);
        Assert.False(success);
        Assert.Equal(ErasedSentinel.Value, plain);

        var (pSuccess, pPlain) = await accessor.TryDecryptAsync(evt.ParentActorIdEncrypted, subjectId);
        Assert.False(pSuccess);
        Assert.Equal(ErasedSentinel.Value, pPlain);
    }

    [Fact]
    public async Task Encrypt_after_tombstone_refuses_per_ADR0038_contract()
    {
        var (keyStore, _, handler, _) = BuildSut();
        const string subjectId = "student-erase-delta";

        // First grant — key materialised.
        _ = await handler.HandleAsync(new GrantConsent(
            subjectId, AgeBand.Adult, ConsentPurpose.MisconceptionDetection,
            Scope: "", GrantedByRole: ActorRole.Student, GrantedByActorId: subjectId,
            GrantedAt: DateTimeOffset.UtcNow, ExpiresAt: null));

        // Erase.
        await keyStore.DeleteAsync(subjectId);

        // Attempt a second grant — the command handler calls EncryptAsync
        // which refuses with InvalidOperationException for a tombstoned subject.
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await handler.HandleAsync(new GrantConsent(
                subjectId, AgeBand.Adult, ConsentPurpose.AiAssistance,
                Scope: "", GrantedByRole: ActorRole.Student, GrantedByActorId: subjectId,
                GrantedAt: DateTimeOffset.UtcNow, ExpiresAt: null)));
    }
}
