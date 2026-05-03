// =============================================================================
// Cena Platform — StripeSetupIntentProviderTests (Phase 1C, §4.0.1 + §5.14)
//
// Network-free unit tests for the Stripe-backed SetupIntent adapter. Subclasses
// the Stripe.Net SetupIntentService and overrides its virtual CreateAsync /
// GetAsync methods so the test can assert the option-payload + idempotency-
// key shape WITHOUT hitting the Stripe sandbox.
//
// Coverage matrix:
//   Construction
//     - Throws when StripeOptions is null
//     - Throws when SecretKey is blank
//
//   CreateSetupIntentAsync
//     - Throws when IdempotencyKey blank
//     - Throws when ParentSubjectIdEncrypted blank
//     - SetupIntentCreateOptions has Usage="off_session"
//     - SetupIntentCreateOptions has PaymentMethodTypes=["card"]
//     - SetupIntentCreateOptions.Metadata carries cena_parent_id
//     - RequestOptions.IdempotencyKey is the caller-supplied value
//     - RequestOptions.ApiKey is the configured secret key
//     - Throws when Stripe returns a SetupIntent with no client_secret
//     - Returns the Stripe-created SetupIntent id + client_secret
//     - Status mapping at create time
//
//   VerifyAndExtractFingerprintAsync
//     - Throws when setupIntentId blank
//     - Get options carry Expand=["payment_method"] (§5.14 server-side re-read)
//     - Status mapping table — every §4.0.1 row is exercised:
//       * succeeded                 → Succeeded + fingerprint extracted
//       * requires_action           → RequiresAction
//       * requires_confirmation     → RequiresAction
//       * requires_payment_method   → RequiresPaymentMethod + decline_code
//       * processing                → Pending
//       * canceled                  → Failed
//       * unknown                   → Failed
//       * succeeded but no fingerprint → Failed (defensive — shouldn't happen)
// =============================================================================

using Cena.Actors.Subscriptions;
using Cena.Actors.Subscriptions.Stripe;
using Stripe;
using Xunit;

namespace Cena.Actors.Tests.Subscriptions.Stripe;

public class StripeSetupIntentProviderTests
{
    private static StripeOptions ValidOptions() => new()
    {
        SecretKey = "sk_test_dummy_local_only",
        WebhookSigningSecret = "whsec_dummy_local_only",
        PriceIds = new StripePriceIdMap
        {
            BasicMonthly = "price_basic_m", BasicAnnual = "price_basic_a",
            PlusMonthly = "price_plus_m", PlusAnnual = "price_plus_a",
            PremiumMonthly = "price_premium_m", PremiumAnnual = "price_premium_a",
        },
    };

    // ---- Construction -------------------------------------------------------

    [Fact]
    public void Constructor_throws_when_options_null()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new StripeSetupIntentProvider(null!));
    }

    [Fact]
    public void Constructor_throws_when_secret_key_blank()
    {
        var options = ValidOptions();
        options.SecretKey = "";
        Assert.Throws<ArgumentException>(() =>
            new StripeSetupIntentProvider(options));
    }

    [Fact]
    public void Adapter_name_is_stripe()
    {
        var sut = new StripeSetupIntentProvider(
            ValidOptions(), NewSpyService());
        Assert.Equal("stripe", sut.Name);
    }

    // ---- CreateSetupIntentAsync — input validation --------------------------

    [Fact]
    public async Task CreateSetupIntentAsync_throws_when_idempotency_key_blank()
    {
        var spy = NewSpyService();
        var sut = new StripeSetupIntentProvider(ValidOptions(), spy);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            sut.CreateSetupIntentAsync(
                new SetupIntentInitRequest("parent_enc_abc", IdempotencyKey: ""),
                default));
        // Sanity: SDK was NOT invoked.
        Assert.Null(spy.LastCreateOptions);
    }

    [Fact]
    public async Task CreateSetupIntentAsync_throws_when_parent_id_blank()
    {
        var spy = NewSpyService();
        var sut = new StripeSetupIntentProvider(ValidOptions(), spy);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            sut.CreateSetupIntentAsync(
                new SetupIntentInitRequest("", "idem-1"),
                default));
        Assert.Null(spy.LastCreateOptions);
    }

    // ---- CreateSetupIntentAsync — Create call shape -------------------------

    [Fact]
    public async Task CreateSetupIntentAsync_passes_off_session_usage_and_card_only()
    {
        var spy = NewSpyService();
        spy.NextCreateResult = new SetupIntent
        {
            Id = "seti_abc",
            ClientSecret = "seti_abc_secret_xyz",
            Status = "requires_payment_method",
        };
        var sut = new StripeSetupIntentProvider(ValidOptions(), spy);

        await sut.CreateSetupIntentAsync(
            new SetupIntentInitRequest("parent_enc_abc", "idem-create-1"),
            default);

        Assert.NotNull(spy.LastCreateOptions);
        Assert.Equal("off_session", spy.LastCreateOptions!.Usage);
        Assert.NotNull(spy.LastCreateOptions.PaymentMethodTypes);
        Assert.Single(spy.LastCreateOptions.PaymentMethodTypes!);
        Assert.Equal("card", spy.LastCreateOptions.PaymentMethodTypes![0]);
    }

    [Fact]
    public async Task CreateSetupIntentAsync_metadata_carries_parent_id()
    {
        var spy = NewSpyService();
        spy.NextCreateResult = new SetupIntent
        {
            Id = "seti_abc",
            ClientSecret = "seti_abc_secret_xyz",
            Status = "requires_payment_method",
        };
        var sut = new StripeSetupIntentProvider(ValidOptions(), spy);

        await sut.CreateSetupIntentAsync(
            new SetupIntentInitRequest("parent_enc_abc", "idem-create-1"),
            default);

        Assert.NotNull(spy.LastCreateOptions!.Metadata);
        Assert.Equal("parent_enc_abc",
            spy.LastCreateOptions.Metadata!["cena_parent_id"]);
    }

    [Fact]
    public async Task CreateSetupIntentAsync_passes_idempotency_key_via_request_options()
    {
        var spy = NewSpyService();
        spy.NextCreateResult = new SetupIntent
        {
            Id = "seti_abc",
            ClientSecret = "seti_abc_secret_xyz",
            Status = "requires_payment_method",
        };
        var sut = new StripeSetupIntentProvider(ValidOptions(), spy);

        await sut.CreateSetupIntentAsync(
            new SetupIntentInitRequest("parent_enc_abc", "idem-create-key-42"),
            default);

        Assert.NotNull(spy.LastCreateRequestOptions);
        Assert.Equal("idem-create-key-42", spy.LastCreateRequestOptions!.IdempotencyKey);
        Assert.Equal("sk_test_dummy_local_only", spy.LastCreateRequestOptions.ApiKey);
    }

    [Fact]
    public async Task CreateSetupIntentAsync_returns_id_secret_and_status()
    {
        var spy = NewSpyService();
        spy.NextCreateResult = new SetupIntent
        {
            Id = "seti_returned",
            ClientSecret = "seti_returned_secret_xyz",
            Status = "requires_payment_method",
        };
        var sut = new StripeSetupIntentProvider(ValidOptions(), spy);

        var result = await sut.CreateSetupIntentAsync(
            new SetupIntentInitRequest("parent_enc_abc", "idem-1"),
            default);

        Assert.Equal("seti_returned", result.SetupIntentId);
        Assert.Equal("seti_returned_secret_xyz", result.ClientSecret);
        Assert.Equal(SetupIntentStatus.RequiresPaymentMethod, result.Status);
    }

    [Fact]
    public async Task CreateSetupIntentAsync_throws_when_client_secret_missing()
    {
        var spy = NewSpyService();
        spy.NextCreateResult = new SetupIntent
        {
            Id = "seti_returned",
            ClientSecret = null,
            Status = "requires_payment_method",
        };
        var sut = new StripeSetupIntentProvider(ValidOptions(), spy);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.CreateSetupIntentAsync(
                new SetupIntentInitRequest("parent_enc_abc", "idem-1"),
                default));
    }

    // ---- VerifyAndExtractFingerprintAsync — input validation ---------------

    [Fact]
    public async Task VerifyAndExtractFingerprintAsync_throws_when_id_blank()
    {
        var spy = NewSpyService();
        var sut = new StripeSetupIntentProvider(ValidOptions(), spy);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            sut.VerifyAndExtractFingerprintAsync("", default));
        // Sanity: SDK was NOT invoked.
        Assert.Null(spy.LastGetId);
    }

    // ---- VerifyAndExtractFingerprintAsync — Get call shape -----------------

    [Fact]
    public async Task VerifyAndExtractFingerprintAsync_expands_payment_method_for_fingerprint()
    {
        // §5.14 server-side re-read: must Expand payment_method so the
        // card.fingerprint is hydrated server-side rather than requiring
        // a follow-up PaymentMethods.Retrieve call.
        var spy = NewSpyService();
        spy.NextGetResult = SucceededIntent("seti_abc", "fp_card_42", "pm_xyz");
        var sut = new StripeSetupIntentProvider(ValidOptions(), spy);

        await sut.VerifyAndExtractFingerprintAsync("seti_abc", default);

        Assert.Equal("seti_abc", spy.LastGetId);
        Assert.NotNull(spy.LastGetOptions);
        Assert.NotNull(spy.LastGetOptions!.Expand);
        Assert.Contains("payment_method", spy.LastGetOptions.Expand!);
        Assert.NotNull(spy.LastGetRequestOptions);
        Assert.Equal("sk_test_dummy_local_only", spy.LastGetRequestOptions!.ApiKey);
    }

    // ---- §4.0.1 status mapping — every row in the table -------------------

    [Fact]
    public async Task Verify_succeeded_extracts_fingerprint_and_payment_method_id()
    {
        var spy = NewSpyService();
        spy.NextGetResult = SucceededIntent("seti_abc", "fp_card_stable", "pm_xyz");
        var sut = new StripeSetupIntentProvider(ValidOptions(), spy);

        var result = await sut.VerifyAndExtractFingerprintAsync("seti_abc", default);

        Assert.Equal(SetupIntentStatus.Succeeded, result.Status);
        Assert.Equal("fp_card_stable", result.CardFingerprint);
        Assert.Equal("pm_xyz", result.PaymentMethodId);
        Assert.Null(result.DeclineCode);
    }

    [Fact]
    public async Task Verify_succeeded_without_fingerprint_is_terminal_failure()
    {
        // Defensive — Stripe's contract says succeeded SetupIntents always
        // have a card.fingerprint, but if the wire format ever drifts we
        // surface it as a Failed terminal rather than silently writing a
        // trial event with no anti-abuse signal.
        var spy = NewSpyService();
        spy.NextGetResult = new SetupIntent
        {
            Id = "seti_abc",
            Status = "succeeded",
            PaymentMethodId = "pm_xyz",
            PaymentMethod = new PaymentMethod
            {
                Id = "pm_xyz",
                Card = new PaymentMethodCard { Fingerprint = null },
            },
        };
        var sut = new StripeSetupIntentProvider(ValidOptions(), spy);

        var result = await sut.VerifyAndExtractFingerprintAsync("seti_abc", default);

        Assert.Equal(SetupIntentStatus.Failed, result.Status);
        Assert.Null(result.CardFingerprint);
        Assert.Equal("fingerprint_missing", result.DeclineCode);
    }

    [Fact]
    public async Task Verify_requires_action_returns_no_fingerprint()
    {
        var spy = NewSpyService();
        spy.NextGetResult = new SetupIntent { Id = "seti_abc", Status = "requires_action" };
        var sut = new StripeSetupIntentProvider(ValidOptions(), spy);

        var result = await sut.VerifyAndExtractFingerprintAsync("seti_abc", default);

        Assert.Equal(SetupIntentStatus.RequiresAction, result.Status);
        Assert.Null(result.CardFingerprint);
        Assert.Null(result.PaymentMethodId);
    }

    [Fact]
    public async Task Verify_requires_confirmation_also_maps_to_requires_action()
    {
        // Stripe distinguishes requires_confirmation (server-driven flow,
        // pre-confirm) from requires_action (3DS challenge); both translate
        // to the SPA's "complete the next step" UX path so we collapse them.
        var spy = NewSpyService();
        spy.NextGetResult = new SetupIntent { Id = "seti_abc", Status = "requires_confirmation" };
        var sut = new StripeSetupIntentProvider(ValidOptions(), spy);

        var result = await sut.VerifyAndExtractFingerprintAsync("seti_abc", default);

        Assert.Equal(SetupIntentStatus.RequiresAction, result.Status);
    }

    [Fact]
    public async Task Verify_requires_payment_method_surfaces_decline_code()
    {
        var spy = NewSpyService();
        spy.NextGetResult = new SetupIntent
        {
            Id = "seti_abc",
            Status = "requires_payment_method",
            LastSetupError = new StripeError { DeclineCode = "card_declined" },
        };
        var sut = new StripeSetupIntentProvider(ValidOptions(), spy);

        var result = await sut.VerifyAndExtractFingerprintAsync("seti_abc", default);

        Assert.Equal(SetupIntentStatus.RequiresPaymentMethod, result.Status);
        Assert.Equal("card_declined", result.DeclineCode);
        Assert.Null(result.CardFingerprint);
    }

    [Fact]
    public async Task Verify_processing_maps_to_pending()
    {
        var spy = NewSpyService();
        spy.NextGetResult = new SetupIntent { Id = "seti_abc", Status = "processing" };
        var sut = new StripeSetupIntentProvider(ValidOptions(), spy);

        var result = await sut.VerifyAndExtractFingerprintAsync("seti_abc", default);

        Assert.Equal(SetupIntentStatus.Pending, result.Status);
        Assert.Null(result.CardFingerprint);
        Assert.Null(result.DeclineCode);
    }

    [Fact]
    public async Task Verify_canceled_maps_to_terminal_failure()
    {
        var spy = NewSpyService();
        spy.NextGetResult = new SetupIntent { Id = "seti_abc", Status = "canceled" };
        var sut = new StripeSetupIntentProvider(ValidOptions(), spy);

        var result = await sut.VerifyAndExtractFingerprintAsync("seti_abc", default);

        Assert.Equal(SetupIntentStatus.Failed, result.Status);
    }

    [Fact]
    public async Task Verify_unknown_status_maps_to_terminal_failure()
    {
        var spy = NewSpyService();
        spy.NextGetResult = new SetupIntent { Id = "seti_abc", Status = "future_unknown_status" };
        var sut = new StripeSetupIntentProvider(ValidOptions(), spy);

        var result = await sut.VerifyAndExtractFingerprintAsync("seti_abc", default);

        Assert.Equal(SetupIntentStatus.Failed, result.Status);
    }

    [Fact]
    public async Task Verify_falls_back_to_payment_method_id_field_when_expand_was_minimal()
    {
        // Defensive: the SDK sometimes hydrates PaymentMethodId without the
        // expanded PaymentMethod object. Caller still gets the pm id even
        // if PaymentMethod.Id is null on the object graph.
        var spy = NewSpyService();
        spy.NextGetResult = new SetupIntent
        {
            Id = "seti_abc",
            Status = "succeeded",
            PaymentMethodId = "pm_via_id_field",
            PaymentMethod = new PaymentMethod
            {
                Id = null,
                Card = new PaymentMethodCard { Fingerprint = "fp_card_42" },
            },
        };
        var sut = new StripeSetupIntentProvider(ValidOptions(), spy);

        var result = await sut.VerifyAndExtractFingerprintAsync("seti_abc", default);

        Assert.Equal(SetupIntentStatus.Succeeded, result.Status);
        Assert.Equal("pm_via_id_field", result.PaymentMethodId);
    }

    // ---- Helpers ------------------------------------------------------------

    private static SetupIntent SucceededIntent(string id, string fingerprint, string pmId) =>
        new()
        {
            Id = id,
            Status = "succeeded",
            PaymentMethodId = pmId,
            PaymentMethod = new PaymentMethod
            {
                Id = pmId,
                Card = new PaymentMethodCard { Fingerprint = fingerprint },
            },
        };

    private static SpySetupIntentService NewSpyService()
    {
        var client = new global::Stripe.StripeClient("sk_test_dummy_local_only");
        return new SpySetupIntentService(client);
    }

    private sealed class SpySetupIntentService : SetupIntentService
    {
        public SpySetupIntentService(IStripeClient client) : base(client) { }

        public SetupIntentCreateOptions? LastCreateOptions { get; private set; }
        public RequestOptions? LastCreateRequestOptions { get; private set; }
        public SetupIntent NextCreateResult { get; set; } = new();

        public string? LastGetId { get; private set; }
        public SetupIntentGetOptions? LastGetOptions { get; private set; }
        public RequestOptions? LastGetRequestOptions { get; private set; }
        public SetupIntent NextGetResult { get; set; } = new();

        public override Task<SetupIntent> CreateAsync(
            SetupIntentCreateOptions options,
            RequestOptions? requestOptions = null,
            CancellationToken cancellationToken = default)
        {
            LastCreateOptions = options;
            LastCreateRequestOptions = requestOptions;
            return Task.FromResult(NextCreateResult);
        }

        public override Task<SetupIntent> GetAsync(
            string id,
            SetupIntentGetOptions? options = null,
            RequestOptions? requestOptions = null,
            CancellationToken cancellationToken = default)
        {
            LastGetId = id;
            LastGetOptions = options;
            LastGetRequestOptions = requestOptions;
            return Task.FromResult(NextGetResult);
        }
    }
}
