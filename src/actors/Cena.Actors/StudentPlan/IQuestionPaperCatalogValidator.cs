// =============================================================================
// Cena Platform — IQuestionPaperCatalogValidator (prr-243, ADR-0050 §1)
//
// Thin abstraction that lets the StudentPlanCommandHandler ask "is this
// שאלון code real for this (examCode, track)?" without taking a direct
// dependency on the catalog service in Cena.Student.Api.Host (which would
// invert the layering — Actors must not depend on the API host).
//
// The default implementation in this file (AllowAllQuestionPaperCatalogValidator)
// accepts any non-empty paper code. Production wiring replaces it with a
// CatalogBackedQuestionPaperCatalogValidator in the Student API host
// composition root, which consults IExamCatalogService.Current for the
// authoritative (examCode, track) → paper-codes map from the YAML catalog
// (contracts/exam-catalog/*.yml) — see PRR-220 for the catalog DTO shape.
//
// The validator is synchronous on purpose: the catalog snapshot is an
// immutable in-memory record swapped atomically, so the hot-path lookup
// is a dictionary probe. No I/O.
// =============================================================================

namespace Cena.Actors.StudentPlan;

/// <summary>
/// Validates that a Ministry שאלון code is declared in the exam catalog
/// for the given (examCode, track) combination.
/// </summary>
public interface IQuestionPaperCatalogValidator
{
    /// <summary>
    /// True when <paramref name="paperCode"/> is declared as a valid
    /// Ministry question-paper code for the given exam + track. For
    /// non-Bagrut families this is NOT called by the command handler
    /// (Standardized targets forbid paper codes outright), but an
    /// implementation that returns false for unknown codes is safe.
    /// </summary>
    /// <param name="examCode">The catalog primary key (e.g.
    /// <c>BAGRUT_MATH_5U</c>).</param>
    /// <param name="track">The track if present (e.g. <c>5U</c>); null
    /// when the exam has no track concept.</param>
    /// <param name="paperCode">Ministry numeric שאלון code (e.g.
    /// <c>035581</c>).</param>
    bool IsPaperCodeValid(ExamCode examCode, TrackCode? track, string paperCode);
}

/// <summary>
/// Permissive default — accepts any non-empty paper code. Used when the
/// catalog is not wired (tests, migration tool) and as the safety fallback
/// when the catalog service has not finished loading.
/// </summary>
/// <remarks>
/// Intentionally does NOT reject paper codes. The architecture test
/// <c>BagrutFamilyRequiresQuestionPapersTest</c> covers the must-have
/// invariant (Bagrut family MUST have ≥1 paper); catalog membership is a
/// defensive second layer enforced in production via
/// <c>CatalogBackedQuestionPaperCatalogValidator</c>.
/// </remarks>
public sealed class AllowAllQuestionPaperCatalogValidator : IQuestionPaperCatalogValidator
{
    /// <summary>Shared singleton.</summary>
    public static readonly AllowAllQuestionPaperCatalogValidator Instance = new();

    /// <inheritdoc />
    public bool IsPaperCodeValid(ExamCode examCode, TrackCode? track, string paperCode)
        => !string.IsNullOrWhiteSpace(paperCode);
}
