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

    // ── Audit ─────────────────────────────────────────────────────────────
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string UpdatedBy { get; set; } = "system";
}
