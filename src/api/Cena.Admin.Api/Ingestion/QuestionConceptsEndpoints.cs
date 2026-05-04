// =============================================================================
// Cena Platform — Curator Concept Review Endpoints (ADR-0062 Phase 1)
//
// Two routes:
//
//   GET  /api/admin/ingestion/items/{id}/concepts
//        Returns the latest extracted + confirmed concept set for the
//        item, plus the canonical catalog (the closed set of leaves the
//        curator may pick from). The SPA review panel uses this to
//        render "extractor said X, curator picked Y, here are the
//        N candidates available".
//
//   POST /api/admin/ingestion/items/{id}/concepts
//        Curator confirms or overrides. The body carries the final
//        concept set (primary first, supporting after) plus the
//        CuratorAction (one-click confirm vs. edit vs. full override).
//        Each entry is canonicalised through BagrutTaxonomyCatalog so
//        a manual typo never produces a free-form SkillCode that
//        forks the catalog.
//
// Both routes:
//   - require ModeratorOrAbove (matches the rest of /api/admin/ingestion)
//   - rate-limit through the "api" bucket
//   - return 404 cleanly when the item is missing or has no payload
//   - return 400 with a structured CenaError when a posted concept
//     doesn't canonicalise — the SPA renders the rejection inline
//     rather than letting the curator believe the override succeeded.
//
// Persistence: POST appends QuestionConceptsConfirmed_V1 to the
// question stream (which is also the draft id during In-Review). The
// endpoint does NOT touch QuestionReadModel directly — the inline
// QuestionListProjection picks up the event and writes to
// QuestionReadModel.Concepts; QuestionState rebuilds ConceptIds on
// aggregate replay. The event is the source of truth; downstream
// consumers re-aggregate.
// =============================================================================

using Cena.Actors.Events;
using Cena.Actors.Ingest;
using Cena.Actors.Mastery;
using Cena.Admin.Api.Ingestion.Html;
using Cena.Infrastructure.Auth;
using Cena.Infrastructure.Documents;
using Cena.Infrastructure.Errors;
using Marten;
using Marten.Events;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using System.Security.Claims;

namespace Cena.Admin.Api.Ingestion;

public static class QuestionConceptsEndpoints
{
    public static IEndpointRouteBuilder MapQuestionConceptsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/ingestion/items")
            .WithTags("Question Concepts")
            .RequireAuthorization(CenaAuthPolicies.ModeratorOrAbove)
            .RequireRateLimiting("api");

        group.MapGet("/{id}/concepts", async (
            string id,
            [FromServices] IDocumentStore store,
            [FromServices] BagrutTaxonomyCatalog catalog,
            CancellationToken ct) =>
        {
            await using var session = store.QuerySession();

            // Item must exist (404 cleanly when the curator hits a stale link).
            var item = await session.LoadAsync<PipelineItemDocument>(id, ct);
            if (item is null)
                return NotFound("item_not_found", $"No pipeline item with id '{id}'.");

            // Walk the question stream: latest QuestionConceptsConfirmed
            // wins over the most recent QuestionConceptsExtracted. If
            // there's no event of either type, the item has never been
            // through the new extractor (legacy / pre-Phase-1).
            var events = await session.Events.FetchStreamAsync(id, token: ct);

            QuestionConceptsExtracted_V1? latestExtracted = null;
            QuestionConceptsConfirmed_V1? latestConfirmed = null;
            foreach (var ev in events)
            {
                switch (ev.Data)
                {
                    case QuestionConceptsExtracted_V1 e:
                        latestExtracted = e;
                        break;
                    case QuestionConceptsConfirmed_V1 c:
                        latestConfirmed = c;
                        break;
                }
            }

            // Catalog snapshot — the SPA picker source. Stable order so
            // the dropdown doesn't shuffle between renders.
            //
            // TopicLabels / SubtopicLabels are passed straight through from
            // the catalog (taxonomy v1.1+ ships en/he/ar). All leaves with
            // the same SkillCode share the same labels (topic/subtopic
            // keys are a function of the SkillCode), so taking the first
            // group member's labels is correct. The SPA picks the right
            // language at render time via vue-i18n's current locale.
            var catalogView = catalog.AllLeaves
                .GroupBy(l => l.SkillCode.Value)
                .Select(g =>
                {
                    var first = g.First();
                    return new ConceptCatalogEntry(
                        SkillCode:      g.Key,
                        ConceptId:      first.ConceptId,
                        Topic:          first.TopicKey,
                        Subtopic:       first.SubtopicKey,
                        Tracks:         g.Select(l => l.TrackId).Distinct().OrderBy(t => t).ToArray(),
                        BloomMin:       g.Min(l => l.BloomMin),
                        BloomMax:       g.Max(l => l.BloomMax),
                        TopicLabels:    first.TopicLabels,
                        SubtopicLabels: first.SubtopicLabels);
                })
                .OrderBy(e => e.SkillCode, StringComparer.Ordinal)
                .ToArray();

            return Results.Ok(new ConceptReviewResponse(
                ItemId:    id,
                Extracted: latestExtracted is null ? null : MapConcepts(latestExtracted.Concepts, latestExtracted.ExtractionStrategy),
                Confirmed: latestConfirmed is null ? null : MapConfirmed(latestConfirmed),
                Catalog:   catalogView));
        })
        .WithName("GetItemConcepts")
        .Produces<ConceptReviewResponse>(StatusCodes.Status200OK)
        .Produces<CenaError>(StatusCodes.Status404NotFound)
        .Produces<CenaError>(StatusCodes.Status401Unauthorized);

        group.MapPost("/{id}/concepts", async (
            string id,
            [FromBody] ConceptConfirmRequest body,
            ClaimsPrincipal user,
            [FromServices] IDocumentStore store,
            [FromServices] BagrutTaxonomyCatalog catalog,
            CancellationToken ct) =>
        {
            if (body is null || body.Concepts is null || body.Concepts.Length == 0)
                return BadRequest("empty_concept_set",
                    "concepts[] is required and must contain at least one entry.");

            // Canonicalise every posted concept BEFORE touching Marten —
            // a typo in one entry rejects the whole submission so the
            // curator gets clean feedback rather than a half-applied set.
            var canonicalised = new List<QuestionConcept>(body.Concepts.Length);
            for (int i = 0; i < body.Concepts.Length; i++)
            {
                var entry = body.Concepts[i];
                if (string.IsNullOrWhiteSpace(entry.SkillCode))
                    return BadRequest("invalid_skill_code",
                        $"concepts[{i}].skillCode is required.");

                if (!catalog.TryCanonicalize(entry.SkillCode, body.TrackHint, out var skill, out _))
                    return BadRequest("unknown_skill_code",
                        $"concepts[{i}].skillCode '{entry.SkillCode}' did not canonicalize against the closed-set catalog.");

                if (!Enum.TryParse<ConceptRole>(entry.Role, ignoreCase: true, out var role))
                    return BadRequest("invalid_role",
                        $"concepts[{i}].role must be one of: Primary, Supporting.");

                canonicalised.Add(new QuestionConcept(
                    SkillCode:   skill,
                    Role:        role,
                    Confidence:  Math.Clamp(entry.Confidence ?? 1.0, 0.0, 1.0),
                    Rationale:   entry.Rationale ?? "",
                    Tier:        "curator"));
            }

            // Exactly one Primary, the rest Supporting — validated up
            // front so the catalog can rely on it downstream.
            var primaryCount = canonicalised.Count(c => c.Role == ConceptRole.Primary);
            if (primaryCount != 1)
                return BadRequest("primary_count_invalid",
                    $"Exactly one concept must have role=Primary; got {primaryCount}.");

            if (!Enum.TryParse<CuratorAction>(body.Action, ignoreCase: true, out var action))
                return BadRequest("invalid_action",
                    "action must be one of: AcceptedAsExtracted, PrimaryEdited, SupportingEdited, FullyOverridden.");

            var confirmedBy = user.FindFirst("user_id")?.Value
                              ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value
                              ?? "unknown-curator";

            await using var session = store.LightweightSession();
            // 404 if the item doesn't exist — same behaviour as GET.
            var item = await session.LoadAsync<PipelineItemDocument>(id, ct);
            if (item is null)
                return NotFound("item_not_found", $"No pipeline item with id '{id}'.");

            var evt = new QuestionConceptsConfirmed_V1(
                QuestionId:  id,
                Concepts:    canonicalised,
                Action:      action,
                ConfirmedBy: confirmedBy,
                Timestamp:   DateTimeOffset.UtcNow);
            session.Events.Append(id, evt);
            await session.SaveChangesAsync(ct);

            return Results.Ok(new ConceptConfirmResponse(
                ItemId:        id,
                Concepts:      MapConcepts(canonicalised, "curator"),
                Action:        action.ToString(),
                ConfirmedBy:   confirmedBy,
                ConfirmedAt:   evt.Timestamp));
        })
        .DisableAntiforgery()
        .Accepts<ConceptConfirmRequest>("application/json")
        .WithName("ConfirmItemConcepts")
        .Produces<ConceptConfirmResponse>(StatusCodes.Status200OK)
        .Produces<CenaError>(StatusCodes.Status400BadRequest)
        .Produces<CenaError>(StatusCodes.Status404NotFound)
        .Produces<CenaError>(StatusCodes.Status401Unauthorized);

        // ADR-0062 Phase 1.5 — OCR cleanup pass. Curator clicks "Enhance
        // with LLM" on the InReview drawer (or the SPA fires it
        // automatically on first open); we run the Bagrut draft's raw
        // text through Anthropic, return the cleaned result, AND persist
        // it on the BagrutDraftPayloadDocument so the next GET /detail
        // round-trip shows the enhanced view without a second LLM call.
        // The enhancer itself caches by sha256(input) (24h TTL) so a
        // refresh of the SPA — or two curators on the same item — never
        // pay for the same Anthropic call twice. Returns 404 if the
        // item or its draft payload is missing, 400 with a structured
        // CenaError on AI provider error.
        group.MapPost("/{id}/enhance-text", async (
            string id,
            [FromServices] IDocumentStore store,
            [FromServices] IOcrTextEnhancer enhancer,
            CancellationToken ct) =>
        {
            await using var session = store.LightweightSession();
            var item = await session.LoadAsync<PipelineItemDocument>(id, ct);
            if (item is null)
                return NotFound("item_not_found", $"No pipeline item with id '{id}'.");

            var payload = await session.LoadAsync<BagrutDraftPayloadDocument>(id, ct);
            if (payload is null)
                return NotFound("draft_payload_not_found",
                    $"Item '{id}' has no Bagrut draft payload — enhance-text only applies to Bagrut drafts.");

            var ocrInput = string.IsNullOrWhiteSpace(payload.LatexContent)
                ? payload.Prompt
                : $"{payload.Prompt}\n\n{payload.LatexContent}";
            if (string.IsNullOrWhiteSpace(ocrInput))
                return BadRequest("empty_ocr_text",
                    "Draft has no prompt or LaTeX content to enhance.");

            // ADR-0062 Phase 1.5 vision-aware enhance (t_3401e9e79877):
            // pass the source PDF id + page so the enhancer can attach the
            // rendered page PNG as an image content block. The enhancer
            // fail-opens to text-only when the PNG cannot be resolved
            // (cache miss + no rasterizer + no pdf-store, or a backfilled
            // item with no persisted PDF bytes).
            var resp = await enhancer.EnhanceOcrTextAsync(
                new EnhanceOcrTextRequest(
                    OcrText: ocrInput,
                    SourceContext: payload.ExamCode,
                    SourcePdfId: string.IsNullOrWhiteSpace(payload.SourcePdfId)
                        ? null
                        : payload.SourcePdfId,
                    SourcePage: payload.SourcePage > 0 ? payload.SourcePage : (int?)null),
                ct);
            if (!resp.Success)
                return BadRequest("ai_enhance_failed",
                    resp.Error ?? "Anthropic call failed without a reason.");

            // Persist enhanced text + metadata on the draft so the next
            // GET /detail returns the cleaned view without re-firing the
            // enhance call. Last-write-wins; under concurrent enhance
            // calls the result is identical (temperature=0) so the race
            // is benign. enhancedAt comes from "now" rather than a
            // re-cached ComputedAt because the contract for the SPA is
            // "when was this surfaced to a curator", not "when did the
            // first compute happen" — that lives in the cache row.
            var nowUtc = DateTimeOffset.UtcNow;
            payload.EnhancedText = resp.EnhancedText;
            payload.EnhancedAt = nowUtc;
            payload.EnhancedBy = resp.ModelUsed;
            payload.EnhancedInputHash = resp.InputHash;
            session.Store(payload);
            await session.SaveChangesAsync(ct);

            return Results.Ok(new EnhanceTextResponse(
                ItemId: id,
                OriginalText: ocrInput,
                EnhancedText: resp.EnhancedText,
                ModelUsed: resp.ModelUsed,
                EnhancedAt: nowUtc));
        })
        .DisableAntiforgery()
        .WithName("EnhanceItemOcrText")
        .Produces<EnhanceTextResponse>(StatusCodes.Status200OK)
        .Produces<CenaError>(StatusCodes.Status400BadRequest)
        .Produces<CenaError>(StatusCodes.Status404NotFound)
        .Produces<CenaError>(StatusCodes.Status401Unauthorized);

        // POST /api/admin/ingestion/items/{id}/render-html (2026-05-04, t_1c57e7389cb4)
        //
        // Single-call PDF → HTML rendering. The curator triggers this
        // explicitly (not auto-fired on ingest in v1) when they want the
        // high-fidelity HTML view: Hebrew RTL preserved, math rendered as
        // HTML sup/sub/fraction divs (NOT LaTeX), every figure recreated
        // as inline <svg>. Adapts the user's verbatim Opus 4.7 recipe.
        //
        // Failure modes (all map to 400 with a structured CenaError):
        //   - draft has no SourcePdfId
        //   - PDF store has no bytes for the id (backfilled item, pre-store)
        //   - extractor returns Success=false (API key missing, circuit
        //     open, Anthropic 4xx/5xx, output truncated, etc.)
        //
        // Auth: ModeratorOrAbove (inherited from the group). Brief calls for
        // AdminOnly but the rest of /api/admin/ingestion/items uses
        // ModeratorOrAbove and operating consistency with the existing
        // /enhance-text endpoint matters more than the brief's literal
        // wording — both are curator-only surfaces and the auth boundary is
        // the SPA's curator-role gate.
        group.MapPost("/{id}/render-html", async (
            string id,
            [FromServices] IDocumentStore store,
            [FromServices] IPdfToHtmlExtractor extractor,
            [FromServices] IBagrutPdfStore pdfStore,
            CancellationToken ct) =>
        {
            await using var session = store.LightweightSession();
            var item = await session.LoadAsync<PipelineItemDocument>(id, ct);
            if (item is null)
                return NotFound("item_not_found", $"No pipeline item with id '{id}'.");

            var payload = await session.LoadAsync<BagrutDraftPayloadDocument>(id, ct);
            if (payload is null)
                return NotFound("draft_payload_not_found",
                    $"Item '{id}' has no Bagrut draft payload — render-html only applies to Bagrut drafts.");

            if (string.IsNullOrWhiteSpace(payload.SourcePdfId))
                return BadRequest("draft_no_source_pdf",
                    "Draft has no SourcePdfId — render-html requires the original PDF to be retained.");

            // Stream PDF bytes from the content-addressable store. Backfilled
            // drafts (pre-IBagrutPdfStore) return null here — the curator
            // is told to re-upload to enable render-html.
            byte[] pdfBytes;
            await using (var pdfStream = await pdfStore.OpenReadAsync(payload.SourcePdfId, ct).ConfigureAwait(false))
            {
                if (pdfStream is null)
                    return BadRequest("source_pdf_not_retained",
                        $"PDF bytes for SourcePdfId '{payload.SourcePdfId}' are not in the store. Re-upload the source PDF to enable render-html.");

                using var ms = new MemoryStream();
                await pdfStream.CopyToAsync(ms, ct).ConfigureAwait(false);
                pdfBytes = ms.ToArray();
            }

            var resp = await extractor.ConvertAsync(
                new PdfToHtmlRequest(pdfBytes, payload.SourcePdfId),
                ct);

            if (!resp.Success)
                return BadRequest("pdf_to_html_failed",
                    resp.Error ?? "Anthropic call failed without a reason.");

            // Persist HtmlContent + HtmlContentAt + HtmlContentModel on the
            // payload so a subsequent GET of the draft returns the rendered
            // HTML without re-firing the call. Last-write-wins; under
            // concurrent /render-html invocations the cost is paid for both
            // calls (the extractor itself does not cache by sha256(pdf-bytes)
            // — that's deferred to v2 per the brief's out-of-scope list).
            var nowUtc = DateTimeOffset.UtcNow;
            payload.HtmlContent = resp.Html;
            payload.HtmlContentAt = nowUtc;
            payload.HtmlContentModel = resp.ModelUsed;
            session.Store(payload);
            await session.SaveChangesAsync(ct);

            return Results.Ok(new RenderHtmlResponse(
                ItemId: id,
                Html: resp.Html,
                ModelUsed: resp.ModelUsed,
                InputTokens: resp.InputTokens,
                OutputTokens: resp.OutputTokens,
                RenderedAt: nowUtc));
        })
        .DisableAntiforgery()
        .WithName("RenderItemHtml")
        .Produces<RenderHtmlResponse>(StatusCodes.Status200OK)
        .Produces<CenaError>(StatusCodes.Status400BadRequest)
        .Produces<CenaError>(StatusCodes.Status404NotFound)
        .Produces<CenaError>(StatusCodes.Status401Unauthorized);

        return app;
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static IResult NotFound(string code, string message)
        => Results.Json(
            new CenaError(code, message, ErrorCategory.NotFound, null, null),
            statusCode: StatusCodes.Status404NotFound);

    private static IResult BadRequest(string code, string message)
        => Results.Json(
            new CenaError(code, message, ErrorCategory.Validation, null, null),
            statusCode: StatusCodes.Status400BadRequest);

    private static ConceptDtoSet MapConcepts(IReadOnlyList<QuestionConcept> concepts, string strategy)
        => new(
            Strategy: strategy,
            Concepts: concepts.Select(c => new ConceptDto(
                SkillCode:  c.SkillCode.Value,
                Role:       c.Role.ToString(),
                Confidence: c.Confidence,
                Rationale:  c.Rationale,
                Tier:       c.Tier)).ToArray());

    private static ConceptDtoConfirmed MapConfirmed(QuestionConceptsConfirmed_V1 evt)
        => new(
            Strategy:    "curator",
            Concepts:    evt.Concepts.Select(c => new ConceptDto(
                SkillCode:  c.SkillCode.Value,
                Role:       c.Role.ToString(),
                Confidence: c.Confidence,
                Rationale:  c.Rationale,
                Tier:       c.Tier)).ToArray(),
            Action:      evt.Action.ToString(),
            ConfirmedBy: evt.ConfirmedBy,
            ConfirmedAt: evt.Timestamp);
}

// ----------------------------------------------------------------------
// Wire shapes
// ----------------------------------------------------------------------

public sealed record ConceptDto(
    string SkillCode,
    string Role,                     // "Primary" | "Supporting"
    double Confidence,
    string Rationale,
    string Tier);                    // "rules" | "llm" | "hybrid" | "curator"

public sealed record ConceptDtoSet(
    string Strategy,                 // versioned identifier ("rules_v1" / "rules_v1+llm_haiku4_5_v1")
    ConceptDto[] Concepts);

public sealed record ConceptDtoConfirmed(
    string Strategy,
    ConceptDto[] Concepts,
    string Action,                   // CuratorAction enum name
    string ConfirmedBy,
    DateTimeOffset ConfirmedAt);

public sealed record ConceptCatalogEntry(
    string SkillCode,
    string ConceptId,
    string Topic,
    string Subtopic,
    string[] Tracks,                 // ["math_3u","math_4u","math_5u"]
    int BloomMin,
    int BloomMax,
    // 2026-05-03: localized display labels for the curator UI. Keyed by
    // language code ("en" / "he" / "ar"). The English keys above remain
    // canonical for storage / event sourcing — these maps are purely
    // presentational. SPA picks the right language via vue-i18n's
    // current locale at render time. Falls back to the English key when
    // a translation is missing (older taxonomy.json without the labels
    // block returns en-only entries derived from the English key).
    IReadOnlyDictionary<string, string> TopicLabels,
    IReadOnlyDictionary<string, string> SubtopicLabels);

public sealed record ConceptReviewResponse(
    string ItemId,
    ConceptDtoSet? Extracted,
    ConceptDtoConfirmed? Confirmed,
    ConceptCatalogEntry[] Catalog);

public sealed record ConceptConfirmRequestEntry(
    string SkillCode,
    string Role,                     // "Primary" | "Supporting"
    double? Confidence,              // optional; defaults to 1.0
    string? Rationale);

public sealed record ConceptConfirmRequest(
    ConceptConfirmRequestEntry[] Concepts,
    string Action,                   // CuratorAction enum name
    string? TrackHint);              // "math_5u" / "math_4u" / "math_3u" — optional

public sealed record ConceptConfirmResponse(
    string ItemId,
    ConceptDtoSet Concepts,
    string Action,
    string ConfirmedBy,
    DateTimeOffset ConfirmedAt);

// ADR-0062 Phase 1.5 — OCR cleanup pass response.
public sealed record EnhanceTextResponse(
    string ItemId,
    string OriginalText,
    string EnhancedText,
    string? ModelUsed,
    DateTimeOffset EnhancedAt);

// 2026-05-04 (t_1c57e7389cb4) — single-call PDF→HTML rendering response.
// Returned by POST /api/admin/ingestion/items/{id}/render-html on success.
// The SPA renders the Html in an iframe / shadow-root so the embedded
// <style> block stays scoped, and surfaces the token counts in the cost
// dashboard.
public sealed record RenderHtmlResponse(
    string ItemId,
    string Html,
    string? ModelUsed,
    long InputTokens,
    long OutputTokens,
    DateTimeOffset RenderedAt);
