// =============================================================================
// Cena Platform — Typed-steps diagnostic endpoint (EPIC-PRR-J PRR-384)
//
//   POST /api/me/diagnostic/typed-steps
//
// Accessibility-first fallback: student types their work via MathLive and
// the server walks the CAS chain directly — no OCR, no photo pipeline.
// Per persona #8 (dysgraphia-friendly) + #1 (never blame handwriting)
// this path must be indistinguishable from a clean-OCR photo outcome
// downstream; we achieve that by funneling into the SAME
// IStepChainVerifier the photo path uses, with synthesized ExtractedStep
// inputs that pin Confidence = 1.0 (OCR is irrelevant — the student
// authored the LaTeX directly).
//
// Discipline per memory "No stubs — production grade":
//   - Confidence = 1.0 is NOT a stub. It is the correct value for
//     "the human typed this themselves; there is no OCR confidence to
//     report" — same semantics as the CSAT-like "100% confident" that
//     a clean-OCR pass would emit. Validated in tests.
//   - The endpoint does not fabricate bounding-box / photo-hash /
//     OCR-confidence fields; the wire DTO omits them entirely so the
//     typed path never pretends to be a photo path.
//
// Feature gate:
//   Typed-steps is part of the photo-diagnostic family, so gated on the
//   same tier as the photo endpoint. Basic has zero PhotoDiagnostics
//   budget (TierCatalog) — Basic students cannot use typed-steps for
//   the same reason they cannot upload a photo. Plus / Premium have
//   budget. The quota gate is NOT wired in v1 of this endpoint because
//   the photo-diagnostic endpoint does not yet exist either (PRR-350
//   upstream blocked); when it ships, the shared IQuotaGate lands in
//   both paths simultaneously. For now the endpoint requires only
//   authentication so dev + accessibility testing can exercise it
//   without quota-provisioning; the v1 scope is explicit below.
//
// Scope boundary v1:
//   - Accepts 1..MaxSteps steps; rejects empty, rejects index drift.
//   - Calls IStepChainVerifier.VerifyChainAsync directly.
//   - Returns first-failure-index + per-transition trail matching the
//     shape PRR-380 (diagnostic result screen) + PRR-382 (show-my-work
//     drawer) already consume for photo outcomes.
//   - Per-student default-mode preference (task DoD item 2) is deferred
//     to a follow-up: that is a preference-store concern (accommodation
//     profile or student-settings doc), not a diagnostic concern. The
//     typed-steps endpoint here is the "Typed mode end-to-end to CAS
//     works" DoD item (#1).
// =============================================================================

using System.Security.Claims;
using Cena.Actors.Diagnosis.PhotoDiagnostic;
using Cena.Api.Contracts.Diagnostic;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Cena.Student.Api.Host.Endpoints;

/// <summary>
/// Wires POST /api/me/diagnostic/typed-steps onto the IStepChainVerifier
/// without touching the photo-OCR pipeline.
/// </summary>
public static class TypedStepsDiagnosticEndpoint
{
    /// <summary>Upper bound on typed-steps per submission. 40 handles a long
    /// Bagrut-style derivation; above this the student should break the
    /// problem into multiple submissions.</summary>
    public const int MaxSteps = 40;

    /// <summary>Per-step LaTeX length cap. Defensive — protects the CAS
    /// router from a malicious paste of a 10-MB LaTeX blob.</summary>
    public const int MaxStepLatexLength = 2_000;

    /// <summary>Register the endpoint.</summary>
    public static IEndpointRouteBuilder MapTypedStepsDiagnosticEndpoint(
        this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/me/diagnostic/typed-steps", HandleAsync)
            .WithTags("PhotoDiagnostic", "Accessibility")
            .RequireAuthorization()
            .WithName("PostTypedStepsDiagnostic");
        return app;
    }

    private static async Task<IResult> HandleAsync(
        HttpContext http,
        [FromBody] TypedStepsDiagnosticRequest request,
        [FromServices] IStepChainVerifier verifier,
        CancellationToken ct)
    {
        // AuthN: a typed-steps submission must identify its caller so
        // the downstream audit log (PRR-423 accuracy-audit sampler) can
        // record which student authored the step sequence. Sub claim
        // matches the convention every other /api/me/... endpoint uses.
        var studentId = http.User.FindFirstValue("sub")
            ?? http.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(studentId))
        {
            return Results.Unauthorized();
        }

        if (request is null)
        {
            return Results.BadRequest(new { error = "invalid_request" });
        }
        var validation = ValidateRequest(request);
        if (validation is not null) return validation;

        // Build ExtractedStep inputs. Confidence = 1.0 (authored by the
        // student, no OCR). Latex = Canonical — the canonicalizer pre-
        // pass inside StepChainVerifier will still run for PRR-361
        // compliance, but we pre-populate so a verifier that sees a
        // non-empty Canonical honours it (skips the re-canonicalize
        // round-trip).
        var steps = new List<ExtractedStep>(request.Steps.Count);
        for (int i = 0; i < request.Steps.Count; i++)
        {
            var s = request.Steps[i];
            steps.Add(new ExtractedStep(
                Index: i,
                Latex: s.Latex,
                Canonical: s.Latex,
                Confidence: 1.0));
        }

        var chain = await verifier.VerifyChainAsync(steps, ct);

        var dto = new TypedStepsDiagnosticResponse(
            Succeeded: chain.Succeeded,
            FirstFailureIndex: chain.FirstFailureIndex,
            Transitions: chain.Transitions
                .Select(t => new TypedStepTransitionDto(
                    FromStepIndex: t.FromStepIndex,
                    ToStepIndex: t.ToStepIndex,
                    Outcome: t.Outcome.ToString(),
                    Summary: t.Summary))
                .ToList());
        return Results.Ok(dto);
    }

    /// <summary>
    /// Validate the incoming request shape. Returns null when the request
    /// is acceptable; otherwise an already-shaped 400 IResult with a
    /// stable error code. Exposed internal so tests can cover every
    /// rejection branch without spinning up the full web host.
    /// </summary>
    internal static IResult? ValidateRequest(TypedStepsDiagnosticRequest request)
    {
        if (request.Steps is null || request.Steps.Count == 0)
        {
            return Results.BadRequest(new { error = "steps_required" });
        }
        if (request.Steps.Count > MaxSteps)
        {
            return Results.BadRequest(new
            {
                error = "too_many_steps",
                max = MaxSteps,
            });
        }
        for (int i = 0; i < request.Steps.Count; i++)
        {
            var s = request.Steps[i];
            if (s is null || string.IsNullOrWhiteSpace(s.Latex))
            {
                return Results.BadRequest(new
                {
                    error = "empty_step",
                    index = i,
                });
            }
            if (s.Latex.Length > MaxStepLatexLength)
            {
                return Results.BadRequest(new
                {
                    error = "step_too_long",
                    index = i,
                    maxLength = MaxStepLatexLength,
                });
            }
            // Index-contiguity guard: v1 requires 0..N-1 in order. We do
            // not silently re-order or backfill — if the client sends
            // out-of-order indices, that is a client bug worth surfacing.
            if (s.Index != i)
            {
                return Results.BadRequest(new
                {
                    error = "index_out_of_order",
                    expected = i,
                    actual = s.Index,
                });
            }
        }
        return null;
    }
}
