// =============================================================================
// Cena Platform — InMemorySetupIntentProviderTests (Phase 1C, §4.0.1 + §5.25)
//
// Coverage matrix (each of the five §4.0.1 failure modes is exercised end-to-end):
//
//   CreateSetupIntentAsync
//     - throws when IdempotencyKey blank
//     - throws when ParentSubjectIdEncrypted blank
//     - returns same SetupIntent on idempotency-key replay (single create)
//     - SetupIntent id is deterministic from the idempotency key
//     - ClientSecret is in Stripe's expected shape (<id>_secret_...)
//
//   VerifyAndExtractFingerprintAsync — five §4.0.1 failure modes
//     [Succeeded]              fingerprint extracted, deterministic per last4
//     [RequiresAction]         3DS still pending, no fingerprint yet
//     [RequiresPaymentMethod]  card-declined, decline_code surfaced
//     [Pending]                Stripe outage, no fingerprint, retryable
//     [Failed]                 terminal failure
//     [unknown SetupIntent id] → Failed with decline_code "setup_intent_not_found"
//
//   Deterministic fingerprint contract (§5.25)
//     - SHA256("test-card-" + last4) — REAL hash, not a placeholder
//     - Two SetupIntents with the same last4 share a fingerprint (abuse path)
//     - Different last4 values yield distinct fingerprints
// =============================================================================

using System.Security.Cryptography;
using System.Text;
using Cena.Actors.Subscriptions;
using Xunit;

namespace Cena.Actors.Tests.Subscriptions;

public class InMemorySetupIntentProviderTests
{
    // ---- Construction / Create input validation -----------------------------

    [Fact]
    public async Task CreateSetupIntentAsync_throws_when_idempotency_key_blank()
    {
        var sut = new InMemorySetupIntentProvider();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            sut.CreateSetupIntentAsync(
                new SetupIntentInitRequest("parent_enc_abc", IdempotencyKey: ""),
                default));
    }

    [Fact]
    public async Task CreateSetupIntentAsync_throws_when_parent_id_blank()
    {
        var sut = new InMemorySetupIntentProvider();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            sut.CreateSetupIntentAsync(
                new SetupIntentInitRequest(ParentSubjectIdEncrypted: "", IdempotencyKey: "idem-1"),
                default));
    }

    [Fact]
    public async Task CreateSetupIntentAsync_replay_with_same_key_returns_same_setup_intent()
    {
        var sut = new InMemorySetupIntentProvider();
        var request = new SetupIntentInitRequest("parent_enc_abc", "idem-replay-1");

        var first = await sut.CreateSetupIntentAsync(request, default);
        var second = await sut.CreateSetupIntentAsync(request, default);

        Assert.Equal(first.SetupIntentId, second.SetupIntentId);
        Assert.Equal(first.ClientSecret, second.ClientSecret);
        Assert.Equal(first.Status, second.Status);
    }

    [Fact]
    public async Task CreateSetupIntentAsync_default_status_is_requires_payment_method()
    {
        var sut = new InMemorySetupIntentProvider();
        var result = await sut.CreateSetupIntentAsync(
            new SetupIntentInitRequest("parent_enc_abc", "idem-default-1"),
            default);

        Assert.StartsWith("seti_inmem_", result.SetupIntentId);
        Assert.Contains("_secret_", result.ClientSecret);
        // Default scenario is Succeeded on Verify, but Create returns the
        // pre-confirm initial status — RequiresPaymentMethod (the SPA must
        // attach a card before it can succeed).
        Assert.Equal(SetupIntentStatus.RequiresPaymentMethod, result.Status);
    }

    // ---- §4.0.1 failure modes — five distinct paths -------------------------

    [Fact]
    public async Task Verify_succeeded_extracts_deterministic_fingerprint()
    {
        var sut = new InMemorySetupIntentProvider(
            scenarioFactory: _ => new InMemorySetupIntentProvider.TestScenario(
                VerifyStatus: SetupIntentStatus.Succeeded,
                CardLast4: "4242"));

        var init = await sut.CreateSetupIntentAsync(
            new SetupIntentInitRequest("parent_enc_abc", "idem-succ-1"), default);
        var verify = await sut.VerifyAndExtractFingerprintAsync(init.SetupIntentId, default);

        Assert.Equal(SetupIntentStatus.Succeeded, verify.Status);
        Assert.NotNull(verify.CardFingerprint);
        Assert.Equal(ExpectedFingerprint("4242"), verify.CardFingerprint);
        Assert.NotNull(verify.PaymentMethodId);
        Assert.StartsWith("pm_inmem_", verify.PaymentMethodId);
        Assert.Null(verify.DeclineCode);
    }

    [Fact]
    public async Task Verify_requires_action_returns_no_fingerprint()
    {
        var sut = new InMemorySetupIntentProvider(
            scenarioFactory: _ => new InMemorySetupIntentProvider.TestScenario(
                VerifyStatus: SetupIntentStatus.RequiresAction,
                CardLast4: "0002"));

        var init = await sut.CreateSetupIntentAsync(
            new SetupIntentInitRequest("parent_enc_abc", "idem-3ds-1"), default);
        // Init status follows the scenario for non-success cases so the SPA
        // sees the right initial state.
        Assert.Equal(SetupIntentStatus.RequiresAction, init.Status);

        var verify = await sut.VerifyAndExtractFingerprintAsync(init.SetupIntentId, default);
        Assert.Equal(SetupIntentStatus.RequiresAction, verify.Status);
        Assert.Null(verify.CardFingerprint);
        Assert.Null(verify.PaymentMethodId);
    }

    [Fact]
    public async Task Verify_requires_payment_method_surfaces_decline_code()
    {
        var sut = new InMemorySetupIntentProvider(
            scenarioFactory: _ => new InMemorySetupIntentProvider.TestScenario(
                VerifyStatus: SetupIntentStatus.RequiresPaymentMethod,
                CardLast4: "0002",
                DeclineCode: "insufficient_funds"));

        var init = await sut.CreateSetupIntentAsync(
            new SetupIntentInitRequest("parent_enc_abc", "idem-decline-1"), default);
        var verify = await sut.VerifyAndExtractFingerprintAsync(init.SetupIntentId, default);

        Assert.Equal(SetupIntentStatus.RequiresPaymentMethod, verify.Status);
        Assert.Null(verify.CardFingerprint);
        Assert.Null(verify.PaymentMethodId);
        Assert.Equal("insufficient_funds", verify.DeclineCode);
    }

    [Fact]
    public async Task Verify_requires_payment_method_uses_default_decline_code_when_none_supplied()
    {
        var sut = new InMemorySetupIntentProvider(
            scenarioFactory: _ => new InMemorySetupIntentProvider.TestScenario(
                VerifyStatus: SetupIntentStatus.RequiresPaymentMethod,
                CardLast4: "0002"));

        var init = await sut.CreateSetupIntentAsync(
            new SetupIntentInitRequest("parent_enc_abc", "idem-default-decline-1"), default);
        var verify = await sut.VerifyAndExtractFingerprintAsync(init.SetupIntentId, default);

        Assert.Equal("card_declined", verify.DeclineCode);
    }

    [Fact]
    public async Task Verify_pending_returns_no_fingerprint_no_decline_code()
    {
        var sut = new InMemorySetupIntentProvider(
            scenarioFactory: _ => new InMemorySetupIntentProvider.TestScenario(
                VerifyStatus: SetupIntentStatus.Pending,
                CardLast4: "4242"));

        var init = await sut.CreateSetupIntentAsync(
            new SetupIntentInitRequest("parent_enc_abc", "idem-pending-1"), default);
        Assert.Equal(SetupIntentStatus.Pending, init.Status);

        var verify = await sut.VerifyAndExtractFingerprintAsync(init.SetupIntentId, default);
        Assert.Equal(SetupIntentStatus.Pending, verify.Status);
        Assert.Null(verify.CardFingerprint);
        Assert.Null(verify.PaymentMethodId);
        Assert.Null(verify.DeclineCode);
    }

    [Fact]
    public async Task Verify_failed_returns_terminal_with_decline_code()
    {
        var sut = new InMemorySetupIntentProvider(
            scenarioFactory: _ => new InMemorySetupIntentProvider.TestScenario(
                VerifyStatus: SetupIntentStatus.Failed,
                CardLast4: "0002",
                DeclineCode: "authentication_failure"));

        var init = await sut.CreateSetupIntentAsync(
            new SetupIntentInitRequest("parent_enc_abc", "idem-failed-1"), default);
        var verify = await sut.VerifyAndExtractFingerprintAsync(init.SetupIntentId, default);

        Assert.Equal(SetupIntentStatus.Failed, verify.Status);
        Assert.Null(verify.CardFingerprint);
        Assert.Equal("authentication_failure", verify.DeclineCode);
    }

    [Fact]
    public async Task Verify_unknown_setup_intent_id_is_terminal_not_found()
    {
        var sut = new InMemorySetupIntentProvider();
        var verify = await sut.VerifyAndExtractFingerprintAsync(
            "seti_does_not_exist", default);

        Assert.Equal(SetupIntentStatus.Failed, verify.Status);
        Assert.Null(verify.CardFingerprint);
        Assert.Equal("setup_intent_not_found", verify.DeclineCode);
    }

    [Fact]
    public async Task Verify_throws_when_setup_intent_id_blank()
    {
        var sut = new InMemorySetupIntentProvider();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            sut.VerifyAndExtractFingerprintAsync("", default));
    }

    [Fact]
    public async Task SeedScenario_lets_tests_drive_verify_without_create()
    {
        var sut = new InMemorySetupIntentProvider();
        sut.SeedScenario(
            "seti_external_42",
            new InMemorySetupIntentProvider.TestScenario(
                VerifyStatus: SetupIntentStatus.Succeeded,
                CardLast4: "4242"));

        var verify = await sut.VerifyAndExtractFingerprintAsync(
            "seti_external_42", default);
        Assert.Equal(SetupIntentStatus.Succeeded, verify.Status);
        Assert.Equal(ExpectedFingerprint("4242"), verify.CardFingerprint);
    }

    // ---- Deterministic fingerprint contract (§5.25) -------------------------

    [Fact]
    public void ComputeFingerprint_is_sha256_of_test_card_last4()
    {
        var actual = InMemorySetupIntentProvider.ComputeFingerprint("4242");
        var expected = ExpectedFingerprint("4242");
        Assert.Equal(expected, actual);
        // Sanity: 32 bytes → 64 hex chars.
        Assert.Equal(64, actual.Length);
    }

    [Fact]
    public void ComputeFingerprint_is_deterministic_across_calls()
    {
        var a = InMemorySetupIntentProvider.ComputeFingerprint("4242");
        var b = InMemorySetupIntentProvider.ComputeFingerprint("4242");
        Assert.Equal(a, b);
    }

    [Fact]
    public void ComputeFingerprint_is_distinct_per_last4()
    {
        var a = InMemorySetupIntentProvider.ComputeFingerprint("4242");
        var b = InMemorySetupIntentProvider.ComputeFingerprint("4111");
        Assert.NotEqual(a, b);
    }

    [Fact]
    public async Task Two_setup_intents_with_same_last4_share_fingerprint()
    {
        // Abuse-defense path from §5.7: the §5.25 deterministic fingerprint
        // lets tests assert that two trials with the same card collide on
        // the ledger — exactly the production behaviour, no flaky network.
        var sut = new InMemorySetupIntentProvider(
            scenarioFactory: _ => new InMemorySetupIntentProvider.TestScenario(
                VerifyStatus: SetupIntentStatus.Succeeded,
                CardLast4: "4242"));

        var init1 = await sut.CreateSetupIntentAsync(
            new SetupIntentInitRequest("parent_enc_alice", "idem-alice-1"), default);
        var init2 = await sut.CreateSetupIntentAsync(
            new SetupIntentInitRequest("parent_enc_bob", "idem-bob-1"), default);

        Assert.NotEqual(init1.SetupIntentId, init2.SetupIntentId);

        var verify1 = await sut.VerifyAndExtractFingerprintAsync(init1.SetupIntentId, default);
        var verify2 = await sut.VerifyAndExtractFingerprintAsync(init2.SetupIntentId, default);

        Assert.Equal(verify1.CardFingerprint, verify2.CardFingerprint);
    }

    [Fact]
    public async Task Two_setup_intents_with_different_last4_have_distinct_fingerprints()
    {
        var perRequest = new Dictionary<string, string>
        {
            ["idem-card-4242"] = "4242",
            ["idem-card-4111"] = "4111",
        };
        var sut = new InMemorySetupIntentProvider(
            scenarioFactory: req => new InMemorySetupIntentProvider.TestScenario(
                VerifyStatus: SetupIntentStatus.Succeeded,
                CardLast4: perRequest[req.IdempotencyKey]));

        var init1 = await sut.CreateSetupIntentAsync(
            new SetupIntentInitRequest("parent_enc_alice", "idem-card-4242"), default);
        var init2 = await sut.CreateSetupIntentAsync(
            new SetupIntentInitRequest("parent_enc_bob", "idem-card-4111"), default);

        var verify1 = await sut.VerifyAndExtractFingerprintAsync(init1.SetupIntentId, default);
        var verify2 = await sut.VerifyAndExtractFingerprintAsync(init2.SetupIntentId, default);

        Assert.NotEqual(verify1.CardFingerprint, verify2.CardFingerprint);
        Assert.Equal(ExpectedFingerprint("4242"), verify1.CardFingerprint);
        Assert.Equal(ExpectedFingerprint("4111"), verify2.CardFingerprint);
    }

    [Fact]
    public void Adapter_name_is_in_memory()
    {
        Assert.Equal("in-memory", new InMemorySetupIntentProvider().Name);
    }

    // ---- Wire-format invariants (SetupIntentInitResult contract) ------------

    [Fact]
    public async Task ClientSecret_format_matches_stripe_shape()
    {
        var sut = new InMemorySetupIntentProvider();
        var result = await sut.CreateSetupIntentAsync(
            new SetupIntentInitRequest("parent_enc_abc", "idem-shape-1"), default);

        // Stripe's client_secret format: <setup_intent_id>_secret_<random>
        Assert.StartsWith(result.SetupIntentId + "_secret_", result.ClientSecret);
        var parts = result.ClientSecret.Split("_secret_");
        Assert.Equal(2, parts.Length);
        Assert.Equal(result.SetupIntentId, parts[0]);
        Assert.NotEmpty(parts[1]);
    }

    [Fact]
    public void SetupIntentStatus_enum_covers_all_five_failure_modes()
    {
        // Wire-format invariant: every §4.0.1 failure mode has a distinct enum.
        var values = Enum.GetValues<SetupIntentStatus>();
        Assert.Contains(SetupIntentStatus.Succeeded, values);
        Assert.Contains(SetupIntentStatus.RequiresAction, values);
        Assert.Contains(SetupIntentStatus.RequiresPaymentMethod, values);
        Assert.Contains(SetupIntentStatus.Pending, values);
        Assert.Contains(SetupIntentStatus.Failed, values);
        // No silent additions / removals — keep the failure-mode contract pinned.
        Assert.Equal(5, values.Length);
    }

    // ---- Helpers ------------------------------------------------------------

    private static string ExpectedFingerprint(string last4)
    {
        var bytes = Encoding.UTF8.GetBytes($"test-card-{last4}");
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
