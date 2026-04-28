// =============================================================================
// Cena Platform — InMemorySetupIntentProvider (Phase 1C, trial-then-paywall §5.25)
//
// Test/dev fake for IPaymentMethodSetupProvider. Lets CI run the full trial-
// start flow with no Stripe network dependency, deterministically.
//
// Design choices (production-grade per "no stubs" memory 2026-04-11):
//
//   1. Deterministic fingerprint via SHA256("test-card-" + last4) per §5.25.
//      A REAL cryptographic hash, not a magic string — abuse-defense tests
//      can assert that "two trials with the same last4 share a fingerprint"
//      and verify the ledger blocks the second trial accurately.
//
//   2. Per-SetupIntent status injection via Func<string, SetupIntentInitResult>
//      so the five failure modes from §4.0.1 are exercisable end-to-end:
//      tests configure a deterministic outcome for each test scenario.
//
//   3. Idempotency: replaying CreateSetupIntentAsync with the same
//      idempotency key returns the same SetupIntent (mirrors Stripe's
//      idempotency-key contract). Backed by a ConcurrentDictionary so
//      parallel test fixtures don't collide.
//
//   4. Verify is a server-side re-read of the in-memory state set up by the
//      preceding Create call (or an explicit Setup call from a test) —
//      mirrors §5.14's single-source-of-truth rule: the test's verify call
//      reflects what was set, not what the SPA claims.
//
// Why this is "production grade" not a stub:
//   - Real SHA256 hash, not a placeholder string.
//   - All five §4.0.1 failure modes covered by deterministic test scenarios.
//   - Idempotency is real (replay-safe), not a TODO.
//   - Used in dev composition when Stripe is not configured (mirrors
//     SandboxCheckoutSessionProvider's role).
// =============================================================================

using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Cena.Actors.Subscriptions;

/// <summary>
/// Deterministic in-memory <see cref="IPaymentMethodSetupProvider"/> for
/// dev/test composition roots. NOT for production use — register the
/// Stripe-backed adapter via <c>AddStripeSetupIntentIfConfigured</c>.
/// </summary>
public sealed class InMemorySetupIntentProvider : IPaymentMethodSetupProvider
{
    /// <summary>
    /// Deterministic test scenario for a given SetupIntent. The provider
    /// translates the scenario into a Verify result; tests pre-configure the
    /// scenario per SetupIntent id (or per idempotency key) so Create + Verify
    /// behave the way the test needs.
    /// </summary>
    /// <param name="VerifyStatus">Coarse status the verify will return.</param>
    /// <param name="CardLast4">
    /// Last-4 digits of the test card — used to seed the deterministic
    /// fingerprint (SHA256 of <c>"test-card-" + last4</c>). Pass the same
    /// value for two scenarios to test fingerprint-collision abuse paths.
    /// </param>
    /// <param name="DeclineCode">
    /// Optional decline code surfaced when <see cref="VerifyStatus"/> is
    /// <see cref="SetupIntentStatus.RequiresPaymentMethod"/> /
    /// <see cref="SetupIntentStatus.Failed"/>.
    /// </param>
    public sealed record TestScenario(
        SetupIntentStatus VerifyStatus,
        string CardLast4,
        string? DeclineCode = null);

    /// <summary>
    /// Default scenario used when a test has not configured one for a given
    /// idempotency key. Simulates the happy path with last4 = <c>4242</c>.
    /// </summary>
    public static readonly TestScenario DefaultSucceededScenario =
        new(SetupIntentStatus.Succeeded, CardLast4: "4242");

    private readonly ConcurrentDictionary<string, SetupIntentInitResult> _byIdempotencyKey = new();
    private readonly ConcurrentDictionary<string, TestScenario> _scenarioBySetupIntentId = new();
    private readonly ConcurrentDictionary<string, string> _idempotencyKeyById = new();
    private readonly Func<SetupIntentInitRequest, TestScenario>? _scenarioFactory;

    /// <summary>
    /// Construct with an optional per-request scenario factory. When the
    /// factory is null, every Create returns the
    /// <see cref="DefaultSucceededScenario"/>. Tests inject a factory to
    /// drive deterministic outcomes per scenario.
    /// </summary>
    public InMemorySetupIntentProvider(
        Func<SetupIntentInitRequest, TestScenario>? scenarioFactory = null)
    {
        _scenarioFactory = scenarioFactory;
    }

    /// <inheritdoc/>
    public string Name => "in-memory";

    /// <summary>
    /// Pre-seed a scenario for a specific SetupIntent id. Useful when a test
    /// drives Verify directly without going through Create (e.g., simulating
    /// a webhook-first race).
    /// </summary>
    public void SeedScenario(string setupIntentId, TestScenario scenario)
    {
        ArgumentNullException.ThrowIfNull(setupIntentId);
        ArgumentNullException.ThrowIfNull(scenario);
        _scenarioBySetupIntentId[setupIntentId] = scenario;
    }

    /// <inheritdoc/>
    public Task<SetupIntentInitResult> CreateSetupIntentAsync(
        SetupIntentInitRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            throw new ArgumentException(
                "IdempotencyKey is required.", nameof(request));
        }
        if (string.IsNullOrWhiteSpace(request.ParentSubjectIdEncrypted))
        {
            throw new ArgumentException(
                "ParentSubjectIdEncrypted is required.", nameof(request));
        }

        // Idempotency: replays with the same key return the same SetupIntent.
        var result = _byIdempotencyKey.GetOrAdd(request.IdempotencyKey, key =>
        {
            var scenario = _scenarioFactory?.Invoke(request) ?? DefaultSucceededScenario;

            // SetupIntent id derived from the idempotency key for stability —
            // tests that round-trip through Create→Verify can predict the id.
            var setupIntentId = $"seti_inmem_{key}";
            // Stripe-shaped client_secret: <id>_secret_<random>. We use a
            // hash of the key for the random-ish suffix so it's stable per key.
            var clientSecret = $"{setupIntentId}_secret_{Sha256Hex(key)[..16]}";

            // Bind the SetupIntent id back to the scenario + idempotency key
            // so Verify can reach it.
            _scenarioBySetupIntentId[setupIntentId] = scenario;
            _idempotencyKeyById[setupIntentId] = key;

            // Initial create-status: most flows surface RequiresPaymentMethod
            // before the SPA confirms; if the scenario is "Succeeded" we still
            // start at RequiresPaymentMethod and only Verify returns Succeeded
            // (mirrors the real Stripe lifecycle: Create → confirm → succeeded).
            // Scenarios that pre-test the verify-pending paths (Pending /
            // RequiresAction) set the create status accordingly so the SPA
            // sees the right initial state.
            var initStatus = scenario.VerifyStatus switch
            {
                SetupIntentStatus.RequiresAction => SetupIntentStatus.RequiresAction,
                SetupIntentStatus.Pending => SetupIntentStatus.Pending,
                _ => SetupIntentStatus.RequiresPaymentMethod,
            };

            return new SetupIntentInitResult(
                SetupIntentId: setupIntentId,
                ClientSecret: clientSecret,
                Status: initStatus);
        });

        return Task.FromResult(result);
    }

    /// <inheritdoc/>
    public Task<SetupIntentVerifyResult> VerifyAndExtractFingerprintAsync(
        string setupIntentId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(setupIntentId))
        {
            throw new ArgumentException(
                "setupIntentId is required.", nameof(setupIntentId));
        }

        if (!_scenarioBySetupIntentId.TryGetValue(setupIntentId, out var scenario))
        {
            // Unknown SetupIntent — server-side re-read of "this id does not
            // exist" → Failed terminal.
            return Task.FromResult(new SetupIntentVerifyResult(
                Status: SetupIntentStatus.Failed,
                CardFingerprint: null,
                PaymentMethodId: null,
                DeclineCode: "setup_intent_not_found"));
        }

        var result = scenario.VerifyStatus switch
        {
            SetupIntentStatus.Succeeded => new SetupIntentVerifyResult(
                Status: SetupIntentStatus.Succeeded,
                CardFingerprint: ComputeFingerprint(scenario.CardLast4),
                PaymentMethodId: $"pm_inmem_{Sha256Hex(setupIntentId)[..16]}",
                DeclineCode: null),

            SetupIntentStatus.RequiresAction => new SetupIntentVerifyResult(
                Status: SetupIntentStatus.RequiresAction,
                CardFingerprint: null,
                PaymentMethodId: null,
                DeclineCode: scenario.DeclineCode),

            SetupIntentStatus.RequiresPaymentMethod => new SetupIntentVerifyResult(
                Status: SetupIntentStatus.RequiresPaymentMethod,
                CardFingerprint: null,
                PaymentMethodId: null,
                DeclineCode: scenario.DeclineCode ?? "card_declined"),

            SetupIntentStatus.Pending => new SetupIntentVerifyResult(
                Status: SetupIntentStatus.Pending,
                CardFingerprint: null,
                PaymentMethodId: null,
                DeclineCode: null),

            _ => new SetupIntentVerifyResult(
                Status: SetupIntentStatus.Failed,
                CardFingerprint: null,
                PaymentMethodId: null,
                DeclineCode: scenario.DeclineCode ?? "setup_intent_failed"),
        };

        return Task.FromResult(result);
    }

    /// <summary>
    /// Compute the deterministic fingerprint per §5.25:
    /// <c>SHA256("test-card-" + last4)</c>, hex-encoded. Two SetupIntents
    /// with the same <c>last4</c> share a fingerprint by design — abuse-
    /// defense tests assert on this collision.
    /// </summary>
    public static string ComputeFingerprint(string cardLast4)
    {
        ArgumentNullException.ThrowIfNull(cardLast4);
        return Sha256Hex($"test-card-{cardLast4}");
    }

    private static string Sha256Hex(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        var hash = SHA256.HashData(bytes);
        var sb = new StringBuilder(hash.Length * 2);
        foreach (var b in hash)
        {
            sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
        }
        return sb.ToString();
    }
}
