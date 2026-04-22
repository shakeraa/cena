// =============================================================================
// Cena Platform — Experiment manifest + bucketer (EPIC-PRR-I PRR-332)
//
// Deterministic hash-bucketing for A/B tests on the pricing page + Plus
// feature-mix. Pure functions only; the host composes them with the
// logged-in parent id (or an anon stable id).
//
// Per memory "Honest not complimentary": every experiment has a kill-
// switch at the DI-registration layer. A stopped experiment returns
// ControlVariant without further work.
// =============================================================================

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
        var hashInput = $"{ExperimentId}:{subjectKey}";
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(hashInput));
        var bucket = BitConverter.ToUInt32(bytes, 0);
        var totalWeight = Variants.Sum(v => v.Weight);
        if (totalWeight <= 0) return Variants[0];
        var slot = (int)(bucket % (uint)totalWeight);
        var running = 0;
        foreach (var variant in Variants)
        {
            running += variant.Weight;
            if (slot < running) return variant;
        }
        return Variants[^1];
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

    /// <summary>List of registered experiment ids (for admin observability).</summary>
    public IReadOnlyCollection<string> Ids => _experiments.Keys;
}
