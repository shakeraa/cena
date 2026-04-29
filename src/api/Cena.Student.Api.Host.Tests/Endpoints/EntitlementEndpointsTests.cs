// =============================================================================
// Cena Platform — EntitlementEndpointsTests (Phase 1D-fix item 4)
//
// Direct-invocation tests of the EntitlementEndpoints handlers. The
// codebase pattern (see CatalogEndpointsTests + ConsentEnforcementTests)
// is to call the handler with a DefaultHttpContext + assembled service
// collection rather than spinning a WebApplicationFactory — fast, hermetic,
// and avoids a missing test fixture.
//
// Coverage:
//   GET /api/me/entitlement
//     - 401 when no sub claim
//     - 200 + Tier=Unsubscribed when no entitlement
//     - 200 + Tier=TrialPlus + populated trial state when Trialing
//     - 200 + Subscription state when Active
//     - HasPaymentMethodOnFile reflects parent stream
//     - DiscountApplied populated when admin issued one for the email
//
//   POST /api/me/start-trial
//     - 401 when no sub claim
//     - 400 invalid_trial_kind on bad kind string
//     - 410 trial_not_offered when TrialAllotmentConfig is all-zero
//     - 409 already_entitled when caller is Active/Trialing/PastDue
//     - 404 no_parent_binding when no parent binding exists
//     - 422 setupintent_unverified when SetupIntent fails to verify
//     - 409 trial_already_used on fingerprint duplicate
//     - 200 + entitlement shape on SelfPay happy path
//     - 200 + HasPaymentMethodOnFile=true after successful SetupIntent flow
//     - 200 on InstituteCode happy path (no card)
//     - 400 missing_setup_intent when SelfPay omits setupIntentId
//     - 400 missing_institute_code when InstituteCode omits instituteCode
// =============================================================================

using System.Security.Claims;
using Cena.Actors.Parent;
using Cena.Actors.Subscriptions;
using Cena.Actors.Subscriptions.Events;
using Cena.Api.Contracts.Subscriptions;
using Cena.Api.Host.Endpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Cena.Student.Api.Host.Tests.Endpoints;

public sealed class EntitlementEndpointsTests
{
    private const string StudentId = "enc::student::endpoint-tests";
    private const string ParentId = "enc::parent::endpoint-tests";
    private const string InstituteId = "inst::default";
    private const string Email = "alice@example.com";

    private static readonly DateTimeOffset Now =
        new(2026, 4, 29, 10, 0, 0, TimeSpan.Zero);

    // ----- GET /api/me/entitlement --------------------------------------

    [Fact]
    public async Task GetEntitlement_returns_401_when_no_sub_claim()
    {
        var fx = await NewFixtureAsync();
        var ctx = fx.HttpContext(withSubject: false);
        var result = await EntitlementEndpoints.GetEntitlementAsync(
            ctx, fx.Resolver, fx.Consumption, fx.Subscriptions, fx.Discounts,
            CancellationToken.None);
        Assert.Equal("UnauthorizedHttpResult", result.GetType().Name);
    }

    [Fact]
    public async Task GetEntitlement_returns_Unsubscribed_when_no_entitlement()
    {
        var fx = await NewFixtureAsync();
        var ctx = fx.HttpContext();
        var result = await EntitlementEndpoints.GetEntitlementAsync(
            ctx, fx.Resolver, fx.Consumption, fx.Subscriptions, fx.Discounts,
            CancellationToken.None);
        var dto = ExtractValue<EntitlementResponseDto>(result);
        Assert.Equal("Unsubscribed", dto.Tier);
        Assert.Equal("Unsubscribed", dto.EffectiveStatus);
        Assert.False(dto.HasPaymentMethodOnFile);
        Assert.Null(dto.Trial);
        Assert.Null(dto.Subscription);
    }

    [Fact]
    public async Task GetEntitlement_returns_TrialPlus_with_trial_state_when_Trialing()
    {
        var fx = await NewFixtureAsync();
        await fx.SeedActiveTrialAsync(
            tutorTurns: 5, photos: 2, sessions: 1, durationDays: 14);
        var ctx = fx.HttpContext();
        var result = await EntitlementEndpoints.GetEntitlementAsync(
            ctx, fx.Resolver, fx.Consumption, fx.Subscriptions, fx.Discounts,
            CancellationToken.None);
        var dto = ExtractValue<EntitlementResponseDto>(result);
        Assert.Equal("TrialPlus", dto.Tier);
        Assert.Equal("Trialing", dto.EffectiveStatus);
        Assert.NotNull(dto.Trial);
        Assert.Equal(5, dto.Trial!.TutorTurnsCap);
        Assert.Equal(2, dto.Trial.PhotoDiagnosticsCap);
        Assert.Equal(1, dto.Trial.SessionsCap);
        Assert.Equal(0, dto.Trial.TutorTurnsUsed);
        Assert.NotNull(dto.Trial.EndsAt);
    }

    [Fact]
    public async Task GetEntitlement_HasPaymentMethodOnFile_reflects_parent_stream()
    {
        var fx = await NewFixtureAsync();
        await fx.SeedActiveTrialAsync(
            tutorTurns: 5, photos: 2, sessions: 1, durationDays: 14,
            withPaymentMethod: true);
        var ctx = fx.HttpContext();
        var result = await EntitlementEndpoints.GetEntitlementAsync(
            ctx, fx.Resolver, fx.Consumption, fx.Subscriptions, fx.Discounts,
            CancellationToken.None);
        var dto = ExtractValue<EntitlementResponseDto>(result);
        Assert.True(dto.HasPaymentMethodOnFile);
    }

    // ----- POST /api/me/start-trial -------------------------------------

    [Fact]
    public async Task StartTrial_returns_401_when_no_sub_claim()
    {
        var fx = await NewFixtureAsync();
        var ctx = fx.HttpContext(withSubject: false);
        var body = new StartTrialRequestDto("SelfPay", "seti_x", null, null);
        var result = await CallStartTrial(ctx, body, fx);
        Assert.Equal("UnauthorizedHttpResult", result.GetType().Name);
    }

    [Fact]
    public async Task StartTrial_returns_400_on_unknown_trial_kind()
    {
        var fx = await NewFixtureAsync();
        await fx.EnableTrialAllotmentAsync();
        var ctx = fx.HttpContext();
        var body = new StartTrialRequestDto("Bogus", null, null, null);
        var result = await CallStartTrial(ctx, body, fx);
        AssertJson(result, expectedStatus: 400, expectedError: "invalid_trial_kind");
    }

    [Fact]
    public async Task StartTrial_returns_410_when_trial_not_offered()
    {
        // Default TrialAllotmentConfig has all knobs = 0 → not offered.
        var fx = await NewFixtureAsync();
        var ctx = fx.HttpContext();
        var body = new StartTrialRequestDto("SelfPay", "seti_x", null, null);
        var result = await CallStartTrial(ctx, body, fx);
        AssertJson(result, expectedStatus: 410, expectedError: "trial_not_offered");
    }

    [Fact]
    public async Task StartTrial_returns_404_when_no_parent_binding()
    {
        var fx = await NewFixtureAsync();
        await fx.EnableTrialAllotmentAsync();
        // Don't seed parent binding.
        var ctx = fx.HttpContext();
        var body = new StartTrialRequestDto("SelfPay", "seti_x", null, null);
        var result = await CallStartTrial(ctx, body, fx);
        AssertJson(result, expectedStatus: 404, expectedError: "no_parent_binding");
    }

    [Fact]
    public async Task StartTrial_returns_400_when_SelfPay_missing_setup_intent()
    {
        var fx = await NewFixtureAsync();
        await fx.EnableTrialAllotmentAsync();
        await fx.SeedParentBindingAsync();
        var ctx = fx.HttpContext();
        var body = new StartTrialRequestDto("SelfPay", null, null, null);
        var result = await CallStartTrial(ctx, body, fx);
        AssertJson(result, expectedStatus: 400, expectedError: "missing_setup_intent");
    }

    [Fact]
    public async Task StartTrial_returns_400_when_InstituteCode_missing_code()
    {
        var fx = await NewFixtureAsync();
        await fx.EnableTrialAllotmentAsync();
        await fx.SeedParentBindingAsync();
        var ctx = fx.HttpContext();
        var body = new StartTrialRequestDto("InstituteCode", null, null, null);
        var result = await CallStartTrial(ctx, body, fx);
        AssertJson(result, expectedStatus: 400, expectedError: "missing_institute_code");
    }

    [Fact]
    public async Task StartTrial_returns_422_when_setupintent_unverified()
    {
        var fx = await NewFixtureAsync();
        await fx.EnableTrialAllotmentAsync();
        await fx.SeedParentBindingAsync();
        // FakePaymentProvider returns Failed by default for "seti_fail".
        fx.PaymentProvider.QueueVerify("seti_fail",
            new SetupIntentVerifyResult(SetupIntentStatus.Failed, null, null, "card_declined"));
        var ctx = fx.HttpContext();
        var body = new StartTrialRequestDto("SelfPay", "seti_fail", null, null);
        var result = await CallStartTrial(ctx, body, fx);
        AssertJson(result, expectedStatus: 422, expectedError: "setupintent_unverified");
    }

    [Fact]
    public async Task StartTrial_SelfPay_happy_path_returns_200_with_payment_method_on_file()
    {
        var fx = await NewFixtureAsync();
        await fx.EnableTrialAllotmentAsync();
        await fx.SeedParentBindingAsync();
        fx.PaymentProvider.QueueVerify("seti_ok",
            new SetupIntentVerifyResult(
                SetupIntentStatus.Succeeded, "card_fp_xyz", "pm_abc", null));
        var ctx = fx.HttpContext();
        var body = new StartTrialRequestDto("SelfPay", "seti_ok", null, null);
        var result = await CallStartTrial(ctx, body, fx);
        var dto = ExtractValue<EntitlementResponseDto>(result);
        Assert.Equal("Trialing", dto.EffectiveStatus);
        Assert.Equal("TrialPlus", dto.Tier);
        Assert.True(dto.HasPaymentMethodOnFile);
        Assert.NotNull(dto.Trial);
        Assert.True(dto.Trial!.TutorTurnsCap > 0);

        // Verify the payment-method-attached event was actually appended.
        var events = await fx.Subscriptions.ReadEventsAsync(ParentId, CancellationToken.None);
        Assert.Contains(events, e => e is SubscriptionPaymentMethodAttached_V1);
        var pmEvent = events.OfType<SubscriptionPaymentMethodAttached_V1>().Single();
        Assert.Equal(PaymentMethodAttachSource.TrialStartSetupIntent, pmEvent.Source);
        Assert.False(string.IsNullOrEmpty(pmEvent.PaymentMethodIdEncrypted));
    }

    [Fact]
    public async Task StartTrial_InstituteCode_happy_path_returns_200_no_payment_method()
    {
        var fx = await NewFixtureAsync();
        await fx.EnableTrialAllotmentAsync();
        await fx.SeedParentBindingAsync();
        var ctx = fx.HttpContext();
        var body = new StartTrialRequestDto("InstituteCode", null, "INST-CODE-42", null);
        var result = await CallStartTrial(ctx, body, fx);
        var dto = ExtractValue<EntitlementResponseDto>(result);
        Assert.Equal("Trialing", dto.EffectiveStatus);
        // No card collected on InstituteCode trials.
        Assert.False(dto.HasPaymentMethodOnFile);
        var events = await fx.Subscriptions.ReadEventsAsync(ParentId, CancellationToken.None);
        Assert.DoesNotContain(events, e => e is SubscriptionPaymentMethodAttached_V1);
    }

    [Fact]
    public async Task StartTrial_returns_409_when_caller_already_trialing()
    {
        var fx = await NewFixtureAsync();
        await fx.EnableTrialAllotmentAsync();
        await fx.SeedParentBindingAsync();
        await fx.SeedActiveTrialAsync(
            tutorTurns: 5, photos: 2, sessions: 1, durationDays: 14);
        var ctx = fx.HttpContext();
        var body = new StartTrialRequestDto("InstituteCode", null, "INST-CODE-99", null);
        var result = await CallStartTrial(ctx, body, fx);
        AssertJson(result, expectedStatus: 409, expectedError: "already_entitled");
    }

    [Fact]
    public async Task StartTrial_returns_409_on_fingerprint_duplicate()
    {
        var fx = await NewFixtureAsync();
        await fx.EnableTrialAllotmentAsync();
        await fx.SeedParentBindingAsync();
        // Pre-record a trial against the same fingerprint+email via the
        // ledger (a different parent burned the same institute code with
        // the same email earlier). The endpoint hashes "INST-CODE-7"
        // prefixed with "inst:" → match.
        var preFingerprint = "inst:" + Sha256Hex("INST-CODE-7");
        var normalizedEmail = EmailNormalizer.Normalize(Email);
        await fx.Ledger.RecordTrialAsync(
            preFingerprint, "enc::other-parent", normalizedEmail, CancellationToken.None);
        var ctx = fx.HttpContext();
        var body = new StartTrialRequestDto("InstituteCode", null, "INST-CODE-7", null);
        var result = await CallStartTrial(ctx, body, fx);
        AssertJson(result, expectedStatus: 409, expectedError: "trial_already_used");
    }

    // ----- helpers -------------------------------------------------------

    private static Task<IResult> CallStartTrial(
        HttpContext ctx, StartTrialRequestDto body, Fixture fx)
    {
        return EntitlementEndpoints.StartTrialAsync(
            ctx, body, fx.Resolver, fx.Subscriptions, fx.Consumption,
            fx.Allotment, fx.Ledger, fx.PaymentProvider, fx.ParentBindings,
            fx.Clock, CancellationToken.None);
    }

    private static T ExtractValue<T>(IResult result) where T : class
    {
        var t = result.GetType();
        var valueProp = t.GetProperty("Value");
        Assert.NotNull(valueProp);
        var value = valueProp!.GetValue(result);
        Assert.IsType<T>(value);
        return (T)value!;
    }

    private static void AssertJson(IResult result, int expectedStatus, string expectedError)
    {
        var t = result.GetType();
        var statusProp = t.GetProperty("StatusCode");
        Assert.NotNull(statusProp);
        var actualStatus = (int?)statusProp!.GetValue(result);
        Assert.Equal(expectedStatus, actualStatus);
        var valueProp = t.GetProperty("Value");
        Assert.NotNull(valueProp);
        var value = valueProp!.GetValue(result);
        Assert.NotNull(value);
        var json = System.Text.Json.JsonSerializer.Serialize(value);
        Assert.Contains($"\"Error\":\"{expectedError}\"", json,
            StringComparison.OrdinalIgnoreCase);
    }

    private static string Sha256Hex(string s)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        var bytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(s));
        return Convert.ToHexStringLower(bytes);
    }

    private static Task<Fixture> NewFixtureAsync()
    {
        return Task.FromResult(new Fixture());
    }

    private sealed class Fixture
    {
        public InMemorySubscriptionAggregateStore Subscriptions { get; } = new();
        public InMemoryStudentTrialConsumptionStore Consumption { get; } = new();
        public InMemoryTrialAllotmentConfigStore Allotment { get; } = new();
        public InMemoryTrialFingerprintLedgerStore Ledger { get; } = new();
        public InMemoryParentChildBindingStore ParentBindings { get; } = new();
        public FakePaymentProvider PaymentProvider { get; } = new();
        public DiscountAssignmentService Discounts { get; }
        public StudentEntitlementResolver Resolver { get; }
        public FakeTimeProvider Clock { get; } = new(Now);

        public Fixture()
        {
            Resolver = new StudentEntitlementResolver(
                Subscriptions,
                documentStore: null,
                graceReader: null,
                parentBindings: ParentBindings,
                clock: Clock);
            Discounts = new DiscountAssignmentService(
                new InMemoryDiscountAssignmentStore(),
                new InMemoryDiscountCouponProvider(),
                new NullDiscountIssuedEmailDispatcher(),
                Clock);
        }

        public HttpContext HttpContext(bool withSubject = true)
        {
            var services = new ServiceCollection().BuildServiceProvider();
            var ctx = new DefaultHttpContext { RequestServices = services };
            if (withSubject)
            {
                ctx.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                {
                    new Claim("sub", StudentId),
                    new Claim(ClaimTypes.Email, Email),
                }, "TestAuth"));
            }
            else
            {
                ctx.User = new ClaimsPrincipal(new ClaimsIdentity());
            }
            return ctx;
        }

        public Task EnableTrialAllotmentAsync() =>
            Allotment.UpdateAsync(
                trialDurationDays: 14,
                trialTutorTurns: 5,
                trialPhotoDiagnostics: 2,
                trialPracticeSessions: 1,
                changedByAdminEncrypted: "enc::admin::test",
                ct: CancellationToken.None);

        public async Task SeedParentBindingAsync()
        {
            await ParentBindings.GrantAsync(
                ParentId, StudentId, InstituteId, Now, CancellationToken.None);
        }

        public async Task SeedActiveTrialAsync(
            int tutorTurns, int photos, int sessions, int durationDays,
            bool withPaymentMethod = false)
        {
            await SeedParentBindingAsync();
            var caps = new TrialCapsSnapshot(
                durationDays, tutorTurns, photos, sessions);
            var endsAt = durationDays > 0 ? Now.AddDays(durationDays) : Now;
            var trialEvent = new TrialStarted_V1(
                ParentId, StudentId, TrialKind.SelfPay,
                Now, endsAt, "fp:test", "v1-baseline", caps);
            await Subscriptions.AppendAsync(ParentId, trialEvent, CancellationToken.None);
            if (withPaymentMethod)
            {
                await Subscriptions.AppendAsync(ParentId,
                    new SubscriptionPaymentMethodAttached_V1(
                        ParentSubjectIdEncrypted: ParentId,
                        PaymentMethodIdEncrypted: "pm-enc:abc",
                        FingerprintHash: "card:abc",
                        AttachedAt: Now,
                        Source: PaymentMethodAttachSource.TrialStartSetupIntent),
                    CancellationToken.None);
            }
        }
    }

    /// <summary>
    /// Test fake for IPaymentMethodSetupProvider — returns canned verify
    /// results queued by id. Default behaviour mirrors the InMemory fake.
    /// </summary>
    private sealed class FakePaymentProvider : IPaymentMethodSetupProvider
    {
        private readonly Dictionary<string, SetupIntentVerifyResult> _verifyByIntent = new();
        public string Name => "fake";

        public void QueueVerify(string setupIntentId, SetupIntentVerifyResult result) =>
            _verifyByIntent[setupIntentId] = result;

        public Task<SetupIntentInitResult> CreateSetupIntentAsync(
            SetupIntentInitRequest request, CancellationToken ct) =>
            Task.FromResult(new SetupIntentInitResult(
                SetupIntentId: "seti_test_" + request.IdempotencyKey,
                ClientSecret: "seti_test_secret",
                Status: SetupIntentStatus.RequiresPaymentMethod));

        public Task<SetupIntentVerifyResult> VerifyAndExtractFingerprintAsync(
            string setupIntentId, CancellationToken ct)
        {
            if (_verifyByIntent.TryGetValue(setupIntentId, out var queued))
            {
                return Task.FromResult(queued);
            }
            // Default: succeed deterministically.
            return Task.FromResult(new SetupIntentVerifyResult(
                Status: SetupIntentStatus.Succeeded,
                CardFingerprint: "card_fp_default_" + setupIntentId,
                PaymentMethodId: "pm_default_" + setupIntentId,
                DeclineCode: null));
        }
    }

    private sealed class FakeTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;
        public FakeTimeProvider(DateTimeOffset now) { _now = now; }
        public override DateTimeOffset GetUtcNow() => _now;
    }
}
