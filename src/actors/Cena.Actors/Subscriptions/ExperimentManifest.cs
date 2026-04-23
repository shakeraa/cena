// =============================================================================
// Cena Platform — Experiment manifest + bucketer + resolver (EPIC-PRR-I PRR-332)
//
// Deterministic hash-bucketing for A/B tests on the pricing page + Plus
// feature-mix. Pure functions only; the host composes them with the
// logged-in parent id (or an anon stable id).
//
// Per memory "Honest not complimentary": every experiment has a kill-
// switch at the DI-registration layer. A stopped experiment returns
// the first variant (convention: control) unconditionally.
//
// ---------------------------------------------------------------------------
// GUARDRAIL — ADR-0057 §6 (retail-pricing authority is TierCatalog, not variants)
// ---------------------------------------------------------------------------
// The experiment harness returns a *variant id* (e.g., "plus-dashboard-in"
// vs. "plus-dashboard-out"), NEVER a Money amount, cap count, or
// TierDefinition field. The pricing page reads all price/cap values from
// TierCatalog.cs, which is a code constant that changes only via a PR
// reviewed by the pricing decision-holder.
//
// Allowed experiment effects (consumer-side, keyed by variant id):
//   - Toggle a feature row in the pricing page copy
//   - Show/hide a plan-comparison column
//   - Swap one decoy-column label for another
//   - Route a CTA to a different checkout flow
//
// BANNED experiment effects (call this out in code review if proposed):
//   - Overriding TierCatalog prices in-flight
//   - Overriding usage caps in-flight
//   - Toggling legal/disclosure copy (consumer-protection gate)
//
// The resolver at the bottom of this file returns IDs only. The consumer
// code converts an id to a display decision; it does NOT consume a
// runtime "price override" field from the variant.
// =============================================================================

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Security.Cryptography;
using System.Text;

namespace Cena.Actors.Subscriptions;

/// <summary>One experiment variant with its traffic allocation weight.</summary>
/// <param name="VariantId">Variant name, e.g., "control" | "plus-no-dashboard".</param>
/// <param name="Weight">Relative weight inside the experiment (any positive number).</param>
public sealed record ExperimentVariant(string VariantId, int Weight);

/// <summary>A single experiment. Manifest-driven; live experiments register in DI.</summary>
/// <param name="ExperimentId">Stable experiment id, e.g., "pricing-plus-dashboard-2026-04".</param>
/// <param name="Enabled">Kill-switch. Disabled → returns the first variant unconditionally.</param>
/// <param name="Variants">Variants with weights. At least one control.</param>
public sealed record ExperimentDefinition(
    string ExperimentId,
    bool Enabled,
    IReadOnlyList<ExperimentVariant> Variants)
{
    /// <summary>Bucket a subject into a variant by stable hash.</summary>
    public ExperimentVariant Bucket(string subjectKey)
    {
        ArgumentNullException.ThrowIfNull(subjectKey);
        if (!Enabled || Variants.Count == 0)
        {
            return Variants.Count > 0 ? Variants[0] : new ExperimentVariant("control", 1);
        }
        var totalWeight = Variants.Sum(v => v.Weight);
        if (totalWeight <= 0) return Variants[0];
        var slot = BucketHasher.SlotFor(subjectKey, ExperimentId, totalWeight);
        var running = 0;
        foreach (var variant in Variants)
        {
            running += variant.Weight;
            if (slot < running) return variant;
        }
        return Variants[^1];
    }
}

/// <summary>
/// Deterministic SHA256-based hashing for experiment bucketing.
///
/// Pure, stateless, and shared across <see cref="ExperimentDefinition.Bucket"/>
/// and any future admin-side distribution analytics. Output is uniform to
/// within the salt+subject-key entropy (empirically ~50/50 over 10k subjects,
/// see ExperimentManifestTests).
///
/// Honors the ADR-0057 §6 guardrail above: this class emits *bucket numbers*,
/// NOT pricing values. Consumers convert a bucket (via an <see cref="ExperimentDefinition"/>)
/// into a variant id; that id is the only thing that leaves the Subscriptions
/// bounded context.
/// </summary>
public static class BucketHasher
{
    /// <summary>Default admin-analytics bucket count (0..99 inclusive).</summary>
    public const int DefaultBuckets = 100;

    /// <summary>
    /// Return the bucket [0, buckets) for a (subject, experiment) pair. Used
    /// directly by admin analytics that want a raw distribution view; the
    /// main variant-selection path calls <see cref="SlotFor"/>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if <paramref name="buckets"/> ≤ 0.</exception>
    public static int BucketFor(string subjectKey, string experimentId, int buckets = DefaultBuckets)
    {
        ArgumentNullException.ThrowIfNull(subjectKey);
        ArgumentNullException.ThrowIfNull(experimentId);
        if (buckets <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(buckets), buckets, "bucket count must be positive");
        }
        return (int)(Hash32(subjectKey, experimentId) % (uint)buckets);
    }

    /// <summary>
    /// Return a slot [0, totalWeight) for a (subject, experiment) pair. Used
    /// inside <see cref="ExperimentDefinition.Bucket"/> for weight-respecting
    /// variant selection.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if <paramref name="totalWeight"/> ≤ 0.</exception>
    public static int SlotFor(string subjectKey, string experimentId, int totalWeight)
    {
        ArgumentNullException.ThrowIfNull(subjectKey);
        ArgumentNullException.ThrowIfNull(experimentId);
        if (totalWeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(totalWeight), totalWeight, "total weight must be positive");
        }
        return (int)(Hash32(subjectKey, experimentId) % (uint)totalWeight);
    }

    /// <summary>
    /// Raw 32-bit hash of the <c>experimentId:subjectKey</c> tuple. Stable
    /// across processes and deployments (SHA256 + first 4 bytes as LE uint).
    /// </summary>
    public static uint Hash32(string subjectKey, string experimentId)
    {
        ArgumentNullException.ThrowIfNull(subjectKey);
        ArgumentNullException.ThrowIfNull(experimentId);
        var hashInput = $"{experimentId}:{subjectKey}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(hashInput));
        return BitConverter.ToUInt32(bytes, 0);
    }
}

/// <summary>In-process manifest of registered experiments. Thread-safe for reads.</summary>
public sealed class ExperimentManifest
{
    private readonly Dictionary<string, ExperimentDefinition> _experiments;

    public ExperimentManifest(IEnumerable<ExperimentDefinition> experiments)
    {
        _experiments = experiments?.ToDictionary(e => e.ExperimentId, StringComparer.Ordinal)
            ?? new Dictionary<string, ExperimentDefinition>(StringComparer.Ordinal);
    }

    /// <summary>Bucket a subject into a variant for the named experiment.</summary>
    public ExperimentVariant Bucket(string experimentId, string subjectKey)
    {
        if (!_experiments.TryGetValue(experimentId, out var experiment))
        {
            return new ExperimentVariant("control", 1);
        }
        return experiment.Bucket(subjectKey);
    }

    /// <summary>Lookup a raw experiment definition, or null if not registered.</summary>
    public ExperimentDefinition? Get(string experimentId)
    {
        ArgumentNullException.ThrowIfNull(experimentId);
        return _experiments.TryGetValue(experimentId, out var exp) ? exp : null;
    }

    /// <summary>List of registered experiment ids (for admin observability).</summary>
    public IReadOnlyCollection<string> Ids => _experiments.Keys;
}

/// <summary>
/// Consumer-facing resolver. The pricing endpoint + any future
/// experiment-aware code depends on this interface instead of on
/// <see cref="ExperimentManifest"/> directly, which keeps tests small
/// (mock the interface, ignore the manifest plumbing).
///
/// Contract:
///   - Returns the variant id string if the experiment is registered and
///     enabled.
///   - Returns <c>null</c> if the experiment is not registered (unknown id).
///   - Returns <c>null</c> if the experiment is registered but kill-switched
///     (Enabled == false). Callers treat null as "use the control / default
///     page copy".
///
/// Why null instead of "control"? A killed experiment is architecturally
/// different from an active experiment whose bucket happens to be control:
/// the consumer should NOT log a cohort tag for a killed experiment (it
/// would pollute unit-economics aggregation with zero-signal rows).
/// </summary>
public interface IExperimentVariantResolver
{
    /// <summary>
    /// Resolve the variant id for <paramref name="subjectKey"/> under
    /// <paramref name="experimentId"/>. See interface docs for null semantics.
    /// </summary>
    string? ResolveVariantId(string subjectKey, string experimentId);
}

/// <summary>
/// Default resolver implementation. Thin wrapper over
/// <see cref="ExperimentManifest"/> that enforces the killed-experiment
/// null-return contract described on <see cref="IExperimentVariantResolver"/>.
/// </summary>
public sealed class ManifestExperimentVariantResolver : IExperimentVariantResolver
{
    private readonly ExperimentManifest _manifest;

    public ManifestExperimentVariantResolver(ExperimentManifest manifest)
    {
        _manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
    }

    /// <inheritdoc />
    public string? ResolveVariantId(string subjectKey, string experimentId)
    {
        ArgumentNullException.ThrowIfNull(subjectKey);
        ArgumentNullException.ThrowIfNull(experimentId);
        var def = _manifest.Get(experimentId);
        if (def is null) return null;
        if (!def.Enabled) return null;
        if (def.Variants.Count == 0) return null;
        return def.Bucket(subjectKey).VariantId;
    }
}

/// <summary>
/// DI registration helper for the experiment harness.
///
/// Usage:
/// <code>
///   services.AddExperiments();
///   // or with a pre-built manifest:
///   services.AddExperiments(new[] { new ExperimentDefinition(...) });
/// </code>
///
/// The default registration is an EMPTY manifest — callers that do not
/// opt in to a specific experiment get <c>null</c> from
/// <see cref="IExperimentVariantResolver.ResolveVariantId"/> and render
/// the control page copy. This is production-grade: a host that has not
/// declared an experiment is not "doing A/B testing", and the resolver
/// must reflect that honestly.
///
/// The in-memory manifest is authoritative for single-host deployments.
/// A Marten-backed mutable store (for admin-UI-driven experiment editing)
/// is a follow-up task once an admin-UI exists. The
/// <see cref="IExperimentVariantResolver"/> interface is stable across
/// that swap.
/// </summary>
public static class ExperimentServiceRegistration
{
    /// <summary>Register the experiment harness with an empty manifest.</summary>
    public static IServiceCollection AddExperiments(this IServiceCollection services)
    {
        return services.AddExperiments(Array.Empty<ExperimentDefinition>());
    }

    /// <summary>Register the experiment harness with a seed manifest.</summary>
    public static IServiceCollection AddExperiments(
        this IServiceCollection services,
        IEnumerable<ExperimentDefinition> experiments)
    {
        ArgumentNullException.ThrowIfNull(experiments);
        var materialised = experiments.ToArray();
        services.TryAddSingleton(_ => new ExperimentManifest(materialised));
        services.TryAddSingleton<IExperimentVariantResolver, ManifestExperimentVariantResolver>();
        return services;
    }
}
