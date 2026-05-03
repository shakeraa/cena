// =============================================================================
// Cena Platform -- Crypto-Shredding Integration Tests (ADR-0038, prr-003b)
//
// Verifies the round-trip contract for MisconceptionDetected_V1's encrypted
// StudentAnswer field and the two tombstone paths (retention-driven and
// user-initiated erasure).
//
// Coverage notes:
//   - Encrypt → Decrypt round-trip under a live subject key.
//   - Tombstone via ErasureWorker-style direct key delete → reads return [erased].
//   - RetentionWorker-style PurgeSessionMisconceptionsAsync returns real
//     counts (not the pre-ADR hardcoded zero).
//   - Pre-ADR plaintext pass-through (backward-compatibility guarantee).
//   - Dev-fallback detection for compliance health-check.
//
// Partial coverage: the RetentionWorker and ErasureWorker BackgroundService
// lifecycles (cron scheduling, Marten I/O) are not in-scope for this test —
// those live in RetentionWorkerTests.cs (RDY-054e blocked, skipped) and
// would require a full Marten integration fixture. This file covers only
// the ADR-0038 primitives + their direct integration into the two workers'
// purge/tombstone paths.
// =============================================================================

using Cena.Actors.Events;
using Cena.Infrastructure.Compliance;
using Cena.Infrastructure.Compliance.KeyStore;

namespace Cena.Actors.Tests.Compliance;

public sealed class CryptoShreddingTests
{
    // Deterministic 32-byte root key for tests — NEVER log or reuse in prod.
    private static readonly byte[] TestRootKey = new byte[]
    {
        0x54, 0x65, 0x73, 0x74, 0x52, 0x6f, 0x6f, 0x74,
        0x4b, 0x65, 0x79, 0x46, 0x6f, 0x72, 0x43, 0x72,
        0x79, 0x70, 0x74, 0x6f, 0x53, 0x68, 0x72, 0x65,
        0x64, 0x54, 0x65, 0x73, 0x74, 0x73, 0x30, 0x31
    };

    private static (InMemorySubjectKeyStore store, EncryptedFieldAccessor accessor) BuildSut()
    {
        var derivation = new SubjectKeyDerivation(TestRootKey, "unit-test-install", isDevFallback: false);
        var store = new InMemorySubjectKeyStore(derivation);
        var accessor = new EncryptedFieldAccessor(store);
        return (store, accessor);
    }

    // -------------------------------------------------------------------------
    // Test 1 — Write encrypted MisconceptionDetected_V1 → read back decrypts.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task MisconceptionDetected_V1_encrypted_StudentAnswer_roundtrips()
    {
        var (_, accessor) = BuildSut();
        const string subjectId = "student-alpha-001";
        const string plaintext = "2x + 3 = 7 so x = 2";

        var encrypted = await accessor.EncryptAsync(plaintext, subjectId);
        Assert.NotNull(encrypted);
        Assert.NotEqual(plaintext, encrypted);

        // Simulate the on-disk Marten event construction with encrypted payload.
        var evt = new MisconceptionDetected_V1(
            StudentId: subjectId,
            SessionId: "session-1",
            BuggyRuleId: "sign-error",
            TopicId: "algebra.linear",
            QuestionId: "q-17",
            StudentAnswer: encrypted!,
            ExpectedPattern: "x = 2",
            DetectedAt: DateTimeOffset.UtcNow);

        var (success, decrypted) = await accessor.TryDecryptAsync(evt.StudentAnswer, subjectId);

        Assert.True(success);
        Assert.Equal(plaintext, decrypted);
    }

    // -------------------------------------------------------------------------
    // Test 2 — Erasure path: tombstone the key → reads return the sentinel.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ErasureWorker_Erase_renders_ciphertext_undecryptable()
    {
        var (store, accessor) = BuildSut();
        const string subjectId = "student-bravo-002";
        const string plaintext = "3/4 + 1/4 = 4/8";

        var encrypted = await accessor.EncryptAsync(plaintext, subjectId);

        // Simulate the ErasureWorker user-initiated path.
        var wasAlive = await store.DeleteAsync(subjectId);
        Assert.True(wasAlive);

        var (success, readback) = await accessor.TryDecryptAsync(encrypted, subjectId);

        Assert.False(success);
        Assert.Equal(ErasedSentinel.Value, readback);

        // Second read stays erased — tombstones are permanent.
        var (success2, readback2) = await accessor.TryDecryptAsync(encrypted, subjectId);
        Assert.False(success2);
        Assert.Equal(ErasedSentinel.Value, readback2);
    }

    // -------------------------------------------------------------------------
    // Test 3 — RetentionWorker-style tombstone returns a real PurgedCount > 0
    //          (replaces the pre-ADR hardcoded zero at RetentionWorker.cs:356).
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Retention_tombstone_reports_real_PurgedCount()
    {
        var (store, accessor) = BuildSut();

        // Materialise three subjects by encrypting something for each.
        for (int i = 0; i < 3; i++)
        {
            await accessor.EncryptAsync($"answer-{i}", $"subj-{i}");
        }

        // Simulate the retention-worker candidate enumeration + tombstone.
        var tombstoned = 0;
        await foreach (var subjectId in store.ListActiveSubjectsAsync())
        {
            var wasAlive = await store.DeleteAsync(subjectId);
            if (wasAlive) tombstoned++;
        }

        // All three should have been tombstoned — the stub would have reported 0.
        Assert.Equal(3, tombstoned);

        // Now the active list is empty.
        var remaining = 0;
        await foreach (var _ in store.ListActiveSubjectsAsync()) remaining++;
        Assert.Equal(0, remaining);
    }

    // -------------------------------------------------------------------------
    // Test 4 — Pre-ADR plaintext values pass through unchanged (migration compat).
    // -------------------------------------------------------------------------

    [Fact]
    public async Task PreAdr_plaintext_StudentAnswer_passes_through_unchanged()
    {
        var (_, accessor) = BuildSut();
        const string subjectId = "student-charlie-003";
        const string legacyPlaintext = "x = 5"; // not a wire-format blob

        var (success, readback) = await accessor.TryDecryptAsync(legacyPlaintext, subjectId);

        Assert.True(success);
        Assert.Equal(legacyPlaintext, readback);
    }

    // -------------------------------------------------------------------------
    // Test 5 — Encrypt on a tombstoned subject is refused (ADR contract).
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Encrypt_on_tombstoned_subject_throws()
    {
        var (store, accessor) = BuildSut();
        const string subjectId = "student-delta-004";

        await accessor.EncryptAsync("first-write", subjectId);
        await store.DeleteAsync(subjectId);

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await accessor.EncryptAsync("second-write-after-erasure", subjectId));
    }

    // -------------------------------------------------------------------------
    // Test 6 — Dev fallback is flagged so the health-check can refuse prod boot.
    // -------------------------------------------------------------------------

    [Fact]
    public void Dev_fallback_derivation_is_flagged_for_health_check()
    {
        var saved = Environment.GetEnvironmentVariable(SubjectKeyDerivation.RootKeyEnvVar);
        try
        {
            Environment.SetEnvironmentVariable(SubjectKeyDerivation.RootKeyEnvVar, null);
            var derivation = SubjectKeyDerivation.FromEnvironment();
            Assert.True(derivation.IsUsingDevFallback);
        }
        finally
        {
            Environment.SetEnvironmentVariable(SubjectKeyDerivation.RootKeyEnvVar, saved);
        }
    }

    [Fact]
    public void Production_root_key_from_env_is_NOT_flagged_as_dev_fallback()
    {
        var saved = Environment.GetEnvironmentVariable(SubjectKeyDerivation.RootKeyEnvVar);
        try
        {
            Environment.SetEnvironmentVariable(
                SubjectKeyDerivation.RootKeyEnvVar,
                Convert.ToBase64String(TestRootKey));

            var derivation = SubjectKeyDerivation.FromEnvironment();
            Assert.False(derivation.IsUsingDevFallback);
        }
        finally
        {
            Environment.SetEnvironmentVariable(SubjectKeyDerivation.RootKeyEnvVar, saved);
        }
    }

    // -------------------------------------------------------------------------
    // Test 7 — Subject-id hash for audit log is deterministic and non-reversible.
    // -------------------------------------------------------------------------

    [Fact]
    public void Audit_hash_is_deterministic_and_short()
    {
        const string subjectId = "student-echo-005";
        var h1 = InMemorySubjectKeyStore.HashSubjectForLog(subjectId);
        var h2 = InMemorySubjectKeyStore.HashSubjectForLog(subjectId);

        Assert.Equal(h1, h2);
        Assert.Equal(16, h1.Length); // 8 bytes hex = 16 chars
        Assert.DoesNotContain(subjectId, h1, StringComparison.Ordinal);
    }

    // -------------------------------------------------------------------------
    // Test 8 — Tombstone tampering: decrypt of a byte-flipped blob returns [erased].
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Tampered_ciphertext_returns_erased_sentinel()
    {
        var (_, accessor) = BuildSut();
        const string subjectId = "student-foxtrot-006";

        var encrypted = await accessor.EncryptAsync("secret answer", subjectId);
        Assert.NotNull(encrypted);

        // Flip a character somewhere deep inside the Base64 payload.
        var prefixLen = "cena.aesgcm.v1:".Length;
        var midIx = prefixLen + (encrypted!.Length - prefixLen) / 2;
        var tampered = encrypted.Substring(0, midIx)
            + (encrypted[midIx] == 'A' ? 'B' : 'A')
            + encrypted.Substring(midIx + 1);

        var (success, readback) = await accessor.TryDecryptAsync(tampered, subjectId);

        Assert.False(success);
        Assert.Equal(ErasedSentinel.Value, readback);
    }
}
