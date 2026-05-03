// =============================================================================
// Cena Platform — Typed-steps diagnostic DTOs (EPIC-PRR-J PRR-384)
//
// Wire surface for the OCR-skipping CAS path: a student types their step
// sequence via MathLive and submits directly — bypassing the photo /
// OCR pipeline entirely. This is the accessibility-first fallback for
// dysgraphia + consistently-low-OCR-confidence students (persona #8).
//
// Why a dedicated endpoint + DTO (rather than re-using the photo-
// diagnostic request shape): the photo pipeline carries OCR confidence,
// per-step bounding boxes, and raw-image metadata. A typed submission
// has none of those — conflating the shapes would either force the
// typed client to fabricate Confidence=1.0 / bbox=∅ / mime="text/latex"
// (stubs — banned by memory) or bloat the photo DTO with optional
// fields that are meaningful only for one of the two paths. Separate
// wire shape, shared downstream CAS machinery via IStepChainVerifier.
//
// Warm-framing memory discipline: copy rendered on top of this wire
// format is owned by the Vue layer (memory "Ship-gate banned terms"
// + ADR-0048). The wire itself is neutral — status codes + first-
// failure index + per-transition outcomes, no "handwriting unclear"
// language, no time-pressure fields.
// =============================================================================

namespace Cena.Api.Contracts.Diagnostic;

/// <summary>
/// One step the student typed in MathLive. The LaTeX string is the
/// canonical surface form — no OCR, no bounding boxes. <paramref name="Index"/>
/// is 0-based and MUST be contiguous starting at 0.
/// </summary>
public sealed record TypedStepInputDto(
    int Index,
    string Latex);

/// <summary>
/// Request body for POST /api/me/diagnostic/typed-steps. The locale field
/// carries the student's active locale so downstream narration (when
/// composed with the misconception taxonomy) renders in the right
/// language; unused by pure CAS verification.
/// </summary>
public sealed record TypedStepsDiagnosticRequest(
    IReadOnlyList<TypedStepInputDto> Steps,
    string Locale = "en");

/// <summary>
/// One transition in the verification walk. Mirrors
/// <c>StepTransitionResult</c> but drops the internal <c>CasResult</c>
/// to keep the wire surface narrow — the client only needs the outcome
/// verdict + first failing transition index.
/// </summary>
public sealed record TypedStepTransitionDto(
    int FromStepIndex,
    int ToStepIndex,
    /// <summary>"Valid" | "Wrong" | "UnfollowableSkip" | "LowConfidence".</summary>
    string Outcome,
    string Summary);

/// <summary>
/// Response from POST /api/me/diagnostic/typed-steps. Same logical shape
/// as the CAS chain trace but without OCR / photo-pipeline fields.
/// </summary>
/// <param name="Succeeded">
/// True when the whole chain verified. Equivalent to
/// <see cref="FirstFailureIndex"/> is null.
/// </param>
/// <param name="FirstFailureIndex">
/// Index of the first wrong / unfollowable transition, or null on success.
/// Powers the "first-wrong-step" UX on the Vue diagnostic result screen
/// (PRR-380) — the typed-steps path reuses the same result screen.
/// </param>
/// <param name="Transitions">
/// Full per-transition trail. Client renders this as the expandable
/// "show my work" drawer (PRR-382 / CasChainExporter) when asked.
/// </param>
public sealed record TypedStepsDiagnosticResponse(
    bool Succeeded,
    int? FirstFailureIndex,
    IReadOnlyList<TypedStepTransitionDto> Transitions);
