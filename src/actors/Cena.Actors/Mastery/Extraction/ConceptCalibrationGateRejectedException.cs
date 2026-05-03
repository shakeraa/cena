// =============================================================================
// Cena Platform — Concept Calibration Gate exception (ADR-0062 Phase 1)
//
// Thrown by QuestionBankService.PublishAsync when an item that went
// through the new extraction pipeline (has QuestionConceptsExtracted_V1)
// has not yet been curator-confirmed (lacks QuestionConceptsConfirmed_V1)
// AND the calibration phase is still active (count < CalibrationThreshold).
//
// The endpoint layer maps this to a 409 Conflict with a structured
// CenaError so the SPA renders a "Concept review required before publish"
// banner pointing the curator to /api/admin/ingestion/items/{id}/concepts.
// =============================================================================

namespace Cena.Actors.Mastery.Extraction;

public sealed class ConceptCalibrationGateRejectedException : Exception
{
    public string QuestionId { get; }
    public int ConfirmedCount { get; }
    public int Threshold { get; }

    public ConceptCalibrationGateRejectedException(
        string questionId, int confirmedCount, int threshold)
        : base(
            $"Cannot publish question '{questionId}': calibration phase active " +
            $"({confirmedCount}/{threshold} curator-confirmed). " +
            "POST /api/admin/ingestion/items/{id}/concepts to confirm before publish.")
    {
        QuestionId = questionId;
        ConfirmedCount = confirmedCount;
        Threshold = threshold;
    }
}
