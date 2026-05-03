// =============================================================================
// Cena Platform — LLM pricing table (prr-046)
//
// Loads per-model input/output $/MTok from contracts/llm/routing-config.yaml
// §1 (models:) and exposes a resolver from model_id → (input, output) rates
// in $ per million tokens.
//
// WHY load from YAML instead of hard-coding constants:
//   Pricing is a policy decision owned by finops + product, not by the code.
//   contracts/llm/routing-config.yaml is promoted to "architectural primitive"
//   by ADR-0026 §1 and is the only place cost data should live. Hard-coded
//   pricing drifts silently when vendors change rates; loading from the YAML
//   makes the single source of truth authoritative.
//
// WHY fail-loud on missing entries:
//   Task DoD non-negotiable: "No stubs. If pricing numbers aren't decided,
//   extract them to routing-config.yaml and fail loudly on missing entries."
//   A missing model row means a new model landed in code without a finops
//   review; the right response is a startup failure, not a silent $0 cost
//   metric that misleads the dashboard.
//
// WHY a direct YamlDotNet parse and not a full config binder:
//   The routing-config.yaml schema is large and most of it (rate_limits,
//   circuit_breaker, PII handling) is not relevant to cost calculation.
//   Binding only the `models:` section keeps this file small and reduces the
//   surface area for schema drift to break cost emission.
// =============================================================================

using YamlDotNet.RepresentationModel;

namespace Cena.Infrastructure.Llm;

/// <summary>
/// Per-model input/output pricing in USD per million tokens, resolved from
/// <c>contracts/llm/routing-config.yaml</c>.
/// </summary>
public sealed class LlmPricingTable
{
    /// <summary>
    /// Per-model rate record: both values are USD per million tokens (MTok).
    /// </summary>
    public readonly record struct Rate(double InputPerMTok, double OutputPerMTok);

    private readonly IReadOnlyDictionary<string, Rate> _rates;
    private readonly IReadOnlyDictionary<string, double> _featureMonthlyCeilingUsd;

    internal LlmPricingTable(
        IReadOnlyDictionary<string, Rate> rates,
        IReadOnlyDictionary<string, double> featureMonthlyCeilingUsd)
    {
        _rates = rates;
        _featureMonthlyCeilingUsd = featureMonthlyCeilingUsd;
    }

    /// <summary>
    /// The set of model IDs that have pricing rows. Used by the cost metric
    /// adapter to validate at startup and by tests.
    /// </summary>
    public IReadOnlyCollection<string> KnownModelIds => (IReadOnlyCollection<string>)_rates.Keys;

    /// <summary>
    /// Per-feature monthly ceiling (USD). Empty when the YAML omits the
    /// section. The projected-spend alert rule reads these as its threshold
    /// when firing; a missing entry is an explicit finops TODO.
    /// </summary>
    public IReadOnlyDictionary<string, double> FeatureMonthlyCeilingUsd => _featureMonthlyCeilingUsd;

    /// <summary>
    /// Resolve a model_id to its USD per-MTok rates. Throws
    /// <see cref="InvalidOperationException"/> when the model is absent from
    /// the YAML — intentional fail-loud behaviour (task DoD non-negotiable).
    /// </summary>
    public Rate Resolve(string modelId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        if (_rates.TryGetValue(modelId, out var rate)) return rate;
        throw new InvalidOperationException(
            $"LLM pricing not found for model_id='{modelId}'. Add a row under " +
            "contracts/llm/routing-config.yaml §models: with cost_per_input_mtok + " +
            "cost_per_output_mtok — no default pricing is applied (prr-046 fail-loud).");
    }

    /// <summary>
    /// Compute the USD cost for a single LLM call given input/output token
    /// counts. Negative tokens are treated as zero — the Anthropic SDK
    /// occasionally returns missing usage fields on streaming failures and we
    /// do not want a negative cost to corrupt the counter.
    /// </summary>
    public double ComputeCostUsd(string modelId, long inputTokens, long outputTokens)
    {
        var rate = Resolve(modelId);
        var input = Math.Max(0L, inputTokens);
        var output = Math.Max(0L, outputTokens);
        return (input * rate.InputPerMTok + output * rate.OutputPerMTok) / 1_000_000.0;
    }

    // ── YAML loader ───────────────────────────────────────────────────────

    /// <summary>
    /// Parse a routing-config.yaml document into a pricing table. The file
    /// must contain a top-level <c>models:</c> map with per-model
    /// <c>cost_per_input_mtok</c> and <c>cost_per_output_mtok</c> numeric
    /// fields. Optional top-level <c>feature_monthly_ceiling_usd:</c> map
    /// populates the alert thresholds.
    /// </summary>
    public static LlmPricingTable LoadFromYaml(string yamlText)
    {
        ArgumentNullException.ThrowIfNull(yamlText);

        using var reader = new StringReader(yamlText);
        var stream = new YamlStream();
        stream.Load(reader);

        if (stream.Documents.Count == 0)
        {
            throw new InvalidOperationException(
                "routing-config.yaml is empty — cannot load pricing table.");
        }

        var root = (YamlMappingNode)stream.Documents[0].RootNode;

        var rates = new Dictionary<string, Rate>(StringComparer.OrdinalIgnoreCase);
        if (!TryGetChildMap(root, "models", out var models))
        {
            throw new InvalidOperationException(
                "routing-config.yaml missing top-level 'models:' map. " +
                "Cannot resolve LLM cost without model pricing rows.");
        }

        foreach (var (_, modelDefNode) in models.Children)
        {
            var modelDef = (YamlMappingNode)modelDefNode;
            var modelId = GetScalarString(modelDef, "model_id")
                ?? throw new InvalidOperationException(
                    "A models: row is missing required 'model_id:' field.");
            var input = GetScalarDouble(modelDef, "cost_per_input_mtok")
                ?? throw new InvalidOperationException(
                    $"models.{modelId} missing required 'cost_per_input_mtok:' field.");
            var output = GetScalarDouble(modelDef, "cost_per_output_mtok")
                ?? throw new InvalidOperationException(
                    $"models.{modelId} missing required 'cost_per_output_mtok:' field.");
            rates[modelId] = new Rate(input, output);
        }

        var ceilings = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        if (TryGetChildMap(root, "feature_monthly_ceiling_usd", out var ceilMap))
        {
            foreach (var (k, v) in ceilMap.Children)
            {
                var feature = ((YamlScalarNode)k).Value;
                if (string.IsNullOrWhiteSpace(feature)) continue;
                if (v is YamlScalarNode scalar
                    && double.TryParse(scalar.Value,
                        System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out var ceil))
                {
                    ceilings[feature] = ceil;
                }
            }
        }

        return new LlmPricingTable(rates, ceilings);
    }

    /// <summary>
    /// Convenience loader: reads a routing-config.yaml file from disk and
    /// parses it. Throws if the file is missing (this is an integration
    /// concern at startup, not a runtime fallback path).
    /// </summary>
    public static LlmPricingTable LoadFromFile(string yamlPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(yamlPath);
        if (!File.Exists(yamlPath))
        {
            throw new FileNotFoundException(
                $"routing-config.yaml not found at '{yamlPath}'. " +
                "Cost metric emission requires this file (ADR-0026 §1).",
                yamlPath);
        }
        return LoadFromYaml(File.ReadAllText(yamlPath));
    }

    private static bool TryGetChildMap(
        YamlMappingNode parent, string key, out YamlMappingNode child)
    {
        foreach (var (k, v) in parent.Children)
        {
            if (k is YamlScalarNode s && s.Value == key && v is YamlMappingNode m)
            {
                child = m;
                return true;
            }
        }
        child = default!;
        return false;
    }

    private static string? GetScalarString(YamlMappingNode node, string key)
    {
        foreach (var (k, v) in node.Children)
        {
            if (k is YamlScalarNode s && s.Value == key && v is YamlScalarNode val)
            {
                return val.Value;
            }
        }
        return null;
    }

    private static double? GetScalarDouble(YamlMappingNode node, string key)
    {
        var raw = GetScalarString(node, key);
        if (raw is null) return null;
        if (double.TryParse(raw,
            System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture,
            out var value))
        {
            return value;
        }
        return null;
    }
}
