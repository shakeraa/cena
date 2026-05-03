// Cena Platform — OCR enhancement cache document (ADR-0062 Phase 1.5)
//
// Why this exists
// ---------------
// `POST /api/admin/ingestion/items/{id}/enhance-text` invokes Anthropic at
// temperature=0 with a fixed system prompt. Identical input therefore
// produces identical output, so a refresh of the SPA — or two curators
// hitting the same item back-to-back — should not pay for the same call
// twice. ~$0.005/call is small individually but real at curator volume.
//
// Cache key
// ---------
// `Id` = `sha256(input)` (lower-hex). NOT keyed by draft id — items with
// genuinely identical OCR text legitimately share a cache hit, and re-OCR
// of the same draft produces a different input hash, which correctly
// invalidates the cached enhancement (the re-OCR hash never lookups the
// stale row). This is by design; do not change to draft-id keying without
// reading the trade-off discussion in the file header of OcrEnhancementCache.cs.
//
// TTL
// ---
// `ExpiresAt` is set to `ComputedAt + 24h` at write-time (absolute, not
// sliding). Sliding TTL on a deterministic cache creates "why is my fresh
// OCR not showing up" debugging surface — the cache stays warm forever
// because every read extends the lease. Absolute expiration eliminates
// that class of confusion.
//
// Cleanup
// -------
// When dropping `mt_doc_pipelineitemdocument` for a test reset, ALSO drop
// `mt_doc_ocrenhancementcache` — the cache is independent of the draft
// lifecycle (by design — cross-draft hits are the point), so a single-
// table truncate leaves orphan rows. The 24h TTL self-heals eventually,
// but explicit cleanup keeps test runs deterministic.

namespace Cena.Infrastructure.Documents;

public sealed class OcrEnhancementCacheDocument
{
    /// <summary>
    /// SHA-256 of the input text, lower-hex. Cache key — items with
    /// identical OCR text share a row by design.
    /// </summary>
    public string Id { get; set; } = "";

    /// <summary>The Anthropic-cleaned output. The whole reason this row exists.</summary>
    public string EnhancedText { get; set; } = "";

    /// <summary>Anthropic model id that produced <see cref="EnhancedText"/> (e.g. "claude-sonnet-4-6").</summary>
    public string ModelUsed { get; set; } = "";

    /// <summary>UTC timestamp when the LLM call completed and this row was written.</summary>
    public DateTimeOffset ComputedAt { get; set; }

    /// <summary>
    /// Absolute expiry (ComputedAt + 24h). Reads check this before treating
    /// the row as a hit. Indexed so a janitor sweep can DELETE WHERE
    /// ExpiresAt &lt; NOW() in a single seek.
    /// </summary>
    public DateTimeOffset ExpiresAt { get; set; }
}
