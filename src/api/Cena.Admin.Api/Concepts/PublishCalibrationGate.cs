// =============================================================================
// Cena Platform — Publish calibration-gate helper (ADR-0062 Phase 1)
// Extracted from QuestionBankService to keep that file under its LOC ratchet
// (ADR-0012). Owns the gate decision + counter call + exception throw so the
// caller stays a one-line invocation.
// =============================================================================

using Cena.Actors.Mastery.Extraction;
using Cena.Actors.Questions;
using Cena.Infrastructure.Errors;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Security.Claims;

namespace Cena.Admin.Api.Concepts;

internal static class PublishCalibrationGate
{
    /// <summary>
    /// True when the question must be curator-confirmed before publish:
    /// went through the new pipeline (HasConceptExtractionEvent), no
    /// confirm yet (HasConceptConfirmEvent false), and the extractor
    /// produced something for the curator to confirm (ConceptIds non-empty).
    /// </summary>
    public static bool RequiresConfirm(QuestionState state)
        => state.HasConceptExtractionEvent
           && !state.HasConceptConfirmEvent
           && state.ConceptIds.Count > 0;

    /// <summary>
    /// Throws <see cref="ConceptCalibrationGateRejectedException"/> when
    /// the gate fires AND calibration is still active. Fail-open on
    /// counter errors per the IConceptCurationCalibrationCounter contract.
    /// </summary>
    public static async Task EnforceAsync(
        string questionId,
        QuestionState state,
        IConceptCurationCalibrationCounter counter,
        ILogger logger)
    {
        if (!RequiresConfirm(state)) return;

        int confirmedCount;
        try
        {
            confirmedCount = await counter.GetConfirmedItemCountAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "[ConceptCalibration] counter failed for question {QuestionId}; allowing publish (fail-open)",
                questionId);
            confirmedCount = counter.CalibrationThreshold;
        }

        if (confirmedCount < counter.CalibrationThreshold)
        {
            logger.LogInformation(
                "[ConceptCalibration] blocking publish for {QuestionId} — calibration {Count}/{Threshold}, no curator confirm",
                questionId, confirmedCount, counter.CalibrationThreshold);
            throw new ConceptCalibrationGateRejectedException(
                questionId, confirmedCount, counter.CalibrationThreshold);
        }
    }

    /// <summary>
    /// Derive a BagrutTaxonomyCatalog track hint from a CreateQuestionRequest's
    /// Grade field. Recognises "math_5u"/"math_4u"/"math_3u" or bare "5u"/
    /// "4u"/"3u". Null otherwise; the catalog falls back to 5u→4u→3u.
    /// </summary>
    public static string? DeriveTrackHint(string? grade)
    {
        if (string.IsNullOrWhiteSpace(grade)) return null;
        var g = grade.Trim().ToLowerInvariant();
        if (g.StartsWith("math_", StringComparison.Ordinal)) return g;
        if (g is "5u" or "4u" or "3u") return $"math_{g}";
        return null;
    }

    /// <summary>
    /// Map the gate-rejection exception to a structured 409 Conflict —
    /// SPA renders "Concept review required" pointing at /concepts.
    /// Centralised here so the publish endpoint stays a 1-line catch.
    /// </summary>
    public static IResult ToConflictResult(this ConceptCalibrationGateRejectedException ex) =>
        Results.Json(new CenaError("concept_calibration_gate", ex.Message,
            ErrorCategory.Validation,
            new Dictionary<string, object>
            {
                ["questionId"] = ex.QuestionId,
                ["confirmedCount"] = ex.ConfirmedCount,
                ["threshold"] = ex.Threshold,
                ["reviewEndpoint"] = $"/api/admin/ingestion/items/{ex.QuestionId}/concepts",
            },
            CorrelationId: null), statusCode: StatusCodes.Status409Conflict);

    /// <summary>
    /// Publish-endpoint handler. Extracted from AdminApiEndpoints.cs so
    /// the route registration stays a one-line MapPost (ADR-0012 LOC
    /// ratchet) and the gate-aware error handling lives next to the
    /// gate it depends on.
    /// </summary>
    public static async Task<IResult> PublishWithGateAsync(
        string id, HttpContext ctx, IQuestionBankService service)
    {
        var userId = ctx.User.FindFirst("sub")?.Value ?? "anonymous";
        try { return await service.PublishAsync(id, userId) ? Results.Ok() : Results.NotFound(); }
        catch (ConceptCalibrationGateRejectedException ex) { return ex.ToConflictResult(); }
    }
}
