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
// Phase 1 turn does NOT yet patch QuestionDocument.PrimaryConceptId /
// ConceptIds — that's the projection layer (separate file). The event
// is the source of truth; downstream consumers re-aggregate.
// =============================================================================

using Cena.Actors.Events;
using Cena.Actors.Ingest;
using Cena.Actors.Mastery;
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
            var catalogView = catalog.AllLeaves
                .GroupBy(l => l.SkillCode.Value)
                .Select(g => new ConceptCatalogEntry(
                    SkillCode:   g.Key,
                    ConceptId:   g.First().ConceptId,
                    Topic:       g.First().TopicKey,
                    Subtopic:    g.First().SubtopicKey,
                    Tracks:      g.Select(l => l.TrackId).Distinct().OrderBy(t => t).ToArray(),
                    BloomMin:    g.Min(l => l.BloomMin),
                    BloomMax:    g.Max(l => l.BloomMax)))
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
        // with LLM" on the InReview drawer; we run the Bagrut draft's
        // raw text through Anthropic, return the cleaned result. The SPA
        // displays it in-place (toggle to original); confirming concepts
        // does NOT depend on this — it's a quality-of-life surface.
        // Returns 404 if the item or its draft payload is missing,
        // 400 with a structured CenaError on AI provider error so the
        // SPA can render the failure inline.
        group.MapPost("/{id}/enhance-text", async (
            string id,
            [FromServices] IDocumentStore store,
            [FromServices] IOcrTextEnhancer enhancer,
            CancellationToken ct) =>
        {
            await using var session = store.QuerySession();
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

            var resp = await enhancer.EnhanceOcrTextAsync(
                new EnhanceOcrTextRequest(ocrInput, payload.ExamCode), ct);
            if (!resp.Success)
                return BadRequest("ai_enhance_failed",
                    resp.Error ?? "Anthropic call failed without a reason.");

            return Results.Ok(new EnhanceTextResponse(
                ItemId: id,
                OriginalText: ocrInput,
                EnhancedText: resp.EnhancedText,
                ModelUsed: resp.ModelUsed,
                EnhancedAt: DateTimeOffset.UtcNow));
        })
        .DisableAntiforgery()
        .WithName("EnhanceItemOcrText")
        .Produces<EnhanceTextResponse>(StatusCodes.Status200OK)
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
    int BloomMax);

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
