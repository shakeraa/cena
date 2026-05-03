// =============================================================================
// Cena Platform — Anthropic supported model closed-set.
//
// Single source of truth for the Anthropic model ids the admin SPA's per-task
// override surface is allowed to choose between. The closed-set discipline
// mirrors BagrutTaxonomyCatalog (ADR-0062 Phase 1): the LLM (or, here, the
// curator) cannot pick a value outside the catalog at all. Validation is
// enforced at the write side of the model-overrides endpoint AND at the
// boundary of <see cref="ModelResolver"/> when materialising the
// AiSettingsDocument. A curator typing a typo in the JSON body, an outdated
// SPA bundle posting a retired model id, or a malformed Marten row that
// somehow reached production all surface as 400 / fail-loud rather than
// silently routing to whatever Anthropic happens to accept.
//
// Pricing rates mirror routing-config.yaml § models: — keep them in sync.
// The SPA dropdown reads <see cref="ToDisplay"/> so each option includes
// the per-Mtok cost ("claude-sonnet-4-6 — $3 input / $15 output per Mtok")
// and curators see the cost impact of an override at decision time.
//
// Why a closed-set INSTEAD of letting curators free-text any string
// Anthropic accepts:
//
//   1. Pricing: every model id has a per-Mtok cost the cost-meter must
//      know about. Free-text would let an unpriced model id slip into
//      production and the cost dashboard would silently show $0.
//   2. Capability drift: not every Anthropic model supports the
//      tool_use parameters the question-generation path relies on. A
//      curator who picks the wrong family fails at request time, not
//      at write time — a worse failure mode than "this model isn't on
//      the supported list".
//   3. Regression test: the SPA dropdown's options come from this list.
//      A typo here surfaces as a TypeScript build break + the closed-set
//      validator on the PUT endpoint refusing the value.
//
// AnthropicConnectionProbe is INTENTIONALLY excluded from the override
// surface — the probe pins itself to the cheapest available model so the
// connection test costs ~$0.0001, not ~$0.001 if it picked Opus. Probe
// model selection is a probe-implementation concern, not a curator-policy
// concern.
// =============================================================================

namespace Cena.Admin.Api.AiSettings;

/// <summary>
/// Per-Mtok cost metadata for an Anthropic model id surfaced in the admin
/// SPA dropdown so curators see the cost impact of an override.
/// </summary>
public sealed record AnthropicModelDisplay(
    string ModelId,
    string DisplayName,
    decimal InputUsdPerMtok,
    decimal OutputUsdPerMtok,
    string Tier);

public static class AnthropicSupportedModels
{
    /// <summary>
    /// Closed-set of Anthropic model ids the admin SPA is allowed to pick.
    /// Order is the SPA dropdown's display order — newest first within tier
    /// (Opus → Sonnet → Haiku) so curators see the highest-quality option at
    /// the top while the cheaper tiers are visually below.
    /// </summary>
    public static IReadOnlyList<AnthropicModelDisplay> All { get; } = new[]
    {
        // Tier: Opus (highest quality, highest cost). Per-Mtok rates here
        // mirror routing-config.yaml § models: aliases — the
        // `All_RowsMatch_RoutingConfigYamlSection1Models` test pins them
        // in lockstep so a YAML edit + missed C# update fails CI.
        new AnthropicModelDisplay(
            ModelId: "claude-opus-4-7",
            DisplayName: "Claude Opus 4.7",
            InputUsdPerMtok: 15.00m,
            OutputUsdPerMtok: 75.00m,
            Tier: "opus"),
        new AnthropicModelDisplay(
            ModelId: "claude-opus-4-6",
            DisplayName: "Claude Opus 4.6",
            InputUsdPerMtok: 5.00m,
            OutputUsdPerMtok: 25.00m,
            Tier: "opus"),

        // Tier: Sonnet (balanced).
        new AnthropicModelDisplay(
            ModelId: "claude-sonnet-4-6",
            DisplayName: "Claude Sonnet 4.6",
            InputUsdPerMtok: 3.00m,
            OutputUsdPerMtok: 15.00m,
            Tier: "sonnet"),
        new AnthropicModelDisplay(
            ModelId: "claude-sonnet-4-5",
            DisplayName: "Claude Sonnet 4.5",
            InputUsdPerMtok: 3.00m,
            OutputUsdPerMtok: 15.00m,
            Tier: "sonnet"),

        // Tier: Haiku (fastest, cheapest).
        new AnthropicModelDisplay(
            ModelId: "claude-haiku-4-5-20251001",
            DisplayName: "Claude Haiku 4.5 (2025-10-01)",
            InputUsdPerMtok: 1.00m,
            OutputUsdPerMtok: 5.00m,
            Tier: "haiku"),
        new AnthropicModelDisplay(
            ModelId: "claude-haiku-4-5-20260101",
            DisplayName: "Claude Haiku 4.5 (2026-01-01)",
            InputUsdPerMtok: 1.00m,
            OutputUsdPerMtok: 5.00m,
            Tier: "haiku"),
    };

    private static readonly Dictionary<string, AnthropicModelDisplay> ById =
        All.ToDictionary(m => m.ModelId, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Returns true when <paramref name="modelId"/> is in the closed-set.
    /// </summary>
    public static bool IsSupported(string? modelId)
    {
        if (string.IsNullOrWhiteSpace(modelId)) return false;
        return ById.ContainsKey(modelId);
    }

    /// <summary>
    /// Resolve <paramref name="modelId"/> to its display + pricing record.
    /// Returns null when the id is not in the closed-set — caller decides
    /// whether to 400 (write-time) or fail-loud (read-time).
    /// </summary>
    public static AnthropicModelDisplay? TryGet(string? modelId)
    {
        if (string.IsNullOrWhiteSpace(modelId)) return null;
        return ById.TryGetValue(modelId, out var display) ? display : null;
    }

    /// <summary>
    /// Resolve the per-call pricing for a model id, falling back to Sonnet
    /// 4.6 for any unknown id (matching the runtime defensive behaviour
    /// already present in AiGenerationService.ResolvePricingFor and
    /// OcrTextEnhancer.ResolvePricingFor — the "unknown" branch is a
    /// guard against a row Marten somehow holds that pre-dates a
    /// retirement, NOT a license to free-text).
    /// </summary>
    public static LlmCallPricing ResolvePricingFor(string? modelId)
    {
        var display = TryGet(modelId);
        return display is null
            ? LlmCallPricing.AnthropicSonnet4_6
            : new LlmCallPricing(display.InputUsdPerMtok, display.OutputUsdPerMtok);
    }
}
