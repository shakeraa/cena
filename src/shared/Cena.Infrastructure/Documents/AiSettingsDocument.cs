// =============================================================================
// Cena Platform — AI Generation Settings Document
//
// Singleton Marten document holding the admin-configured LLM provider state
// (active provider, encrypted Anthropic API key, model id, temperature,
// generation defaults). Replaces the in-memory dictionary in
// AiGenerationService so settings — including the API key — survive admin-api
// restarts and container recreation.
//
// Lives under Cena.Infrastructure.Documents so the shared MartenConfiguration
// can register the schema, and so actor-host's startup
// ApplyAllConfiguredChangesToDatabaseAsync creates the table for every host
// in the stack. Cipher + probe + service stay in Cena.Admin.Api (admin-only
// logic).
//
// The API key is stored as an AES-GCM-256 ciphertext blob in
// HkdfApiKeyCipher wire format ("cena.aesgcm.v1:" + base64). Plaintext never
// lands on disk and is never returned through the GET response — only
// HasApiKey: bool.
// =============================================================================

namespace Cena.Infrastructure.Documents;

public sealed class AiSettingsDocument
{
    public const string SingletonId = "ai-settings-singleton";

    public string Id { get; set; } = SingletonId;

    /// <summary>
    /// String form of the active LLM provider ("Anthropic"). Stored as a
    /// string so adding new providers is an additive schema change with no
    /// Marten migration needed. Mirrors the AiProvider enum in
    /// Cena.Admin.Api but isn't typed against it — admin-only enums must
    /// not leak into the shared infrastructure.
    /// </summary>
    public string ActiveProvider { get; set; } = "Anthropic";

    // ── Anthropic ─────────────────────────────────────────────────────────
    /// <summary>Encrypted API-key blob in HkdfApiKeyCipher wire format
    /// ("cena.aesgcm.v1:" + base64). Empty string when no key is configured.
    /// Never holds plaintext.</summary>
    public string AnthropicApiKeyCipher { get; set; } = "";

    public string AnthropicModelId { get; set; } = "claude-sonnet-4-6";

    public float AnthropicTemperature { get; set; } = 0.5f;

    public string? AnthropicBaseUrl { get; set; }

    public string? AnthropicApiVersion { get; set; }

    public bool AnthropicEnabled { get; set; } = true;

    // ── Generation defaults ───────────────────────────────────────────────
    public string DefaultLanguage { get; set; } = "he";
    public int DefaultBloomsLevel { get; set; } = 3;
    public string DefaultGrade { get; set; } = "4 Units";
    public int QuestionsPerBatch { get; set; } = 5;
    public bool AutoRunQualityGate { get; set; } = true;

    // ── Per-task model overrides (V2 — added 2026-05-03) ──────────────────
    /// <summary>
    /// Curator-configurable per-task model override. Keyed by canonical task
    /// name from <c>contracts/llm/routing-config.yaml</c> (e.g.
    /// <c>concept_extraction</c>, <c>quality_gate</c>, <c>bagrut_segmentation</c>,
    /// <c>ocr_text_enhance</c>, <c>question_generation</c>); value is the
    /// Anthropic model id the curator chose for that task this week.
    /// <para>
    /// Empty map (the default) means every task uses the routing-config
    /// default — backwards-compatible with V1 documents that pre-date this
    /// field. Marten's JSON deserializer fills the property with an empty
    /// dictionary when reading old rows because the property's default
    /// initializer runs on every materialised instance, so no Marten
    /// upcaster is required for additive expansion.
    /// </para>
    /// <para>
    /// Validation (closed-set against <c>AnthropicSupportedModels</c>) is
    /// enforced at the admin endpoint write site, not here — the
    /// Marten layer is the persistence seam, not the validation seam, and
    /// keeping policy out of the document type lets the closed-set list
    /// evolve without touching the schema.
    /// </para>
    /// </summary>
    public Dictionary<string, string> ModelOverridesByTask { get; set; } = new();

    // ── Audit ─────────────────────────────────────────────────────────────
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string UpdatedBy { get; set; } = "system";

    /// <summary>
    /// Last actor that mutated <see cref="ModelOverridesByTask"/> via the
    /// admin endpoint. Surfaces in GET /api/admin/ai/settings/model-overrides
    /// so curators see "Tamar changed quality_gate from Haiku to Sonnet 23
    /// minutes ago". Distinct from <see cref="UpdatedBy"/> because the
    /// generic Update endpoint and the model-overrides endpoint are
    /// separate audit surfaces.
    /// </summary>
    public string? ModelOverridesLastChangedBy { get; set; }

    /// <summary>Last time <see cref="ModelOverridesByTask"/> mutated; null when no overrides have ever been set.</summary>
    public DateTimeOffset? ModelOverridesLastChangedAt { get; set; }
}
