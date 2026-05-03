// =============================================================================
// Cena Platform — SetupIntentPortContractTests (Phase 1C, §4.0.1 + §5.14)
//
// Wire-format invariant tests for the IPaymentMethodSetupProvider port.
// These tests run against the InMemorySetupIntentProvider (no Stripe network
// calls) and pin the contract that both the InMemory fake and the Stripe
// production adapter must honour.
//
// Coverage matrix:
//
//   ClientSecret format invariants (Stripe shape)
//     - ClientSecret starts with SetupIntentId
//     - ClientSecret contains "_secret_"
//     - Parts on either side of "_secret_" are non-empty
//
//   SetupIntentStatus enum coverage
//     - Exactly 5 values (one per §4.0.1 failure mode)
//     - All 5 named values present in the enum
//
//   Fingerprint contract (Succeeded path)
//     - CardFingerprint is non-null on Succeeded
//     - CardFingerprint is 64-char lowercase hex (SHA256)
//
//   Non-fingerprint paths
//     - CardFingerprint is null for RequiresAction
//     - CardFingerprint is null for RequiresPaymentMethod
//     - CardFingerprint is null for Pending
//     - CardFingerprint is null for Failed
//
//   Status-consistent result shapes
//     - Succeeded: fingerprint + paymentMethodId both non-null
//     - Non-Succeeded: fingerprint null
// =============================================================================

using Cena.Actors.Subscriptions;
using Xunit;

namespace Cena.Actors.Tests.Subscriptions;

public class SetupIntentPortContractTests
{
    // ---- ClientSecret format invariants (§4.0.1 Stripe shape) ---------------

    [Fact]
    public async Task ClientSecret_starts_with_setup_intent_id()
    {
        var sut = new InMemorySetupIntentProvider();
        var result = await sut.CreateSetupIntentAsync(
            new SetupIntentInitRequest("parent_enc_contract_1", "idem-contract-1"),
            default);

        Assert.StartsWith(result.SetupIntentId, result.ClientSecret);
    }

    [Fact]
    public async Task ClientSecret_contains_secret_separator()
    {
        var sut = new InMemorySetupIntentProvider();
        var result = await sut.CreateSetupIntentAsync(
            new SetupIntentInitRequest("parent_enc_contract_2", "idem-contract-2"),
            default);

        Assert.Contains("_secret_", result.ClientSecret);
    }

    [Fact]
    public async Task ClientSecret_has_non_empty_parts_on_both_sides_of_separator()
    {
        var sut = new InMemorySetupIntentProvider();
        var result = await sut.CreateSetupIntentAsync(
            new SetupIntentInitRequest("parent_enc_contract_3", "idem-contract-3"),
            default);

        var parts = result.ClientSecret.Split("_secret_", 2);
        Assert.Equal(2, parts.Length);
        Assert.NotEmpty(parts[0]); // SetupIntent id portion
        Assert.NotEmpty(parts[1]); // Random/hash suffix
    }

    // ---- SetupIntentStatus enum coverage (§4.0.1) ---------------------------

    [Fact]
    public void SetupIntentStatus_enum_has_exactly_five_values()
    {
        // Five failure modes per §4.0.1 — no silent additions or removals.
        var values = Enum.GetValues<SetupIntentStatus>();
        Assert.Equal(5, values.Length);
    }

    [Fact]
    public void SetupIntentStatus_enum_contains_all_five_named_modes()
    {
        var values = Enum.GetValues<SetupIntentStatus>();
        Assert.Contains(SetupIntentStatus.Succeeded, values);
        Assert.Contains(SetupIntentStatus.RequiresAction, values);
        Assert.Contains(SetupIntentStatus.RequiresPaymentMethod, values);
        Assert.Contains(SetupIntentStatus.Pending, values);
        Assert.Contains(SetupIntentStatus.Failed, values);
    }

    // ---- Fingerprint contract (Succeeded) -----------------------------------

    [Fact]
    public async Task Succeeded_verify_returns_non_null_fingerprint()
    {
        var sut = new InMemorySetupIntentProvider(
            scenarioFactory: _ => new InMemorySetupIntentProvider.TestScenario(
                VerifyStatus: SetupIntentStatus.Succeeded,
                CardLast4: "4242"));

        var init = await sut.CreateSetupIntentAsync(
            new SetupIntentInitRequest("parent_enc_contract_4", "idem-contract-4"),
            default);
        var verify = await sut.VerifyAndExtractFingerprintAsync(init.SetupIntentId, default);

        Assert.Equal(SetupIntentStatus.Succeeded, verify.Status);
        Assert.NotNull(verify.CardFingerprint);
        Assert.NotEmpty(verify.CardFingerprint);
    }

    [Fact]
    public async Task Succeeded_verify_fingerprint_is_64_char_lowercase_hex()
    {
        // SHA256 produces 32 bytes = 64 hex characters, all lowercase.
        var sut = new InMemorySetupIntentProvider(
            scenarioFactory: _ => new InMemorySetupIntentProvider.TestScenario(
                VerifyStatus: SetupIntentStatus.Succeeded,
                CardLast4: "4242"));

        var init = await sut.CreateSetupIntentAsync(
            new SetupIntentInitRequest("parent_enc_contract_5", "idem-contract-5"),
            default);
        var verify = await sut.VerifyAndExtractFingerprintAsync(init.SetupIntentId, default);

        Assert.NotNull(verify.CardFingerprint);
        Assert.Equal(64, verify.CardFingerprint!.Length);
        // All hex chars are lowercase (0-9, a-f).
        Assert.True(verify.CardFingerprint.All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f')),
            $"Fingerprint should be lowercase hex but was: {verify.CardFingerprint}");
    }

    [Fact]
    public async Task Succeeded_verify_returns_non_null_payment_method_id()
    {
        var sut = new InMemorySetupIntentProvider(
            scenarioFactory: _ => new InMemorySetupIntentProvider.TestScenario(
                VerifyStatus: SetupIntentStatus.Succeeded,
                CardLast4: "4242"));

        var init = await sut.CreateSetupIntentAsync(
            new SetupIntentInitRequest("parent_enc_contract_6", "idem-contract-6"),
            default);
        var verify = await sut.VerifyAndExtractFingerprintAsync(init.SetupIntentId, default);

        Assert.NotNull(verify.PaymentMethodId);
        Assert.NotEmpty(verify.PaymentMethodId);
    }

    // ---- Non-Succeeded paths must NOT carry a fingerprint -------------------

    [Theory]
    [InlineData(SetupIntentStatus.RequiresAction)]
    [InlineData(SetupIntentStatus.RequiresPaymentMethod)]
    [InlineData(SetupIntentStatus.Pending)]
    [InlineData(SetupIntentStatus.Failed)]
    public async Task Non_succeeded_verify_has_null_fingerprint(SetupIntentStatus targetStatus)
    {
        var sut = new InMemorySetupIntentProvider(
            scenarioFactory: _ => new InMemorySetupIntentProvider.TestScenario(
                VerifyStatus: targetStatus,
                CardLast4: "4242"));

        var init = await sut.CreateSetupIntentAsync(
            new SetupIntentInitRequest("parent_enc_contract_7", $"idem-contract-ns-{targetStatus}"),
            default);
        var verify = await sut.VerifyAndExtractFingerprintAsync(init.SetupIntentId, default);

        Assert.Equal(targetStatus, verify.Status);
        Assert.Null(verify.CardFingerprint);
    }

    // ---- IPaymentMethodSetupProvider.Name convention -------------------------

    [Fact]
    public void InMemory_adapter_name_is_in_memory()
    {
        IPaymentMethodSetupProvider sut = new InMemorySetupIntentProvider();
        Assert.Equal("in-memory", sut.Name);
    }
}
