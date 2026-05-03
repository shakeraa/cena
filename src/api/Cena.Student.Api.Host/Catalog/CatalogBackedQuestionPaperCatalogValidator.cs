// =============================================================================
// Cena Platform — CatalogBackedQuestionPaperCatalogValidator (PRR-243)
//
// Production implementation of IQuestionPaperCatalogValidator backed by
// the in-memory ExamCatalogService snapshot. Reads the
// MinistryQuestionPaperCodes list for the (examCode, track) pair and
// accepts any code from that list.
//
// Case-sensitive match against the Ministry numeric strings (e.g.
// "035581") — the YAML catalog stores them verbatim. The track argument
// is used only for defensive cross-check (the catalog key already pins
// the track via the exam_code → track relation), but we still accept a
// caller-supplied track and reject on mismatch to make the invariant
// explicit for migration + tenant-admin flows.
// =============================================================================

using Cena.Actors.StudentPlan;

namespace Cena.Student.Api.Host.Catalog;

/// <summary>
/// Catalog-backed validator. Accepts a paper code if it is declared in
/// the ExamCatalogService snapshot for the given
/// <see cref="Cena.Actors.StudentPlan.ExamCode"/>.
/// </summary>
public sealed class CatalogBackedQuestionPaperCatalogValidator : IQuestionPaperCatalogValidator
{
    private readonly IExamCatalogService _catalog;

    /// <summary>Wire via DI. Requires <see cref="IExamCatalogService"/>.</summary>
    public CatalogBackedQuestionPaperCatalogValidator(IExamCatalogService catalog)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    }

    /// <inheritdoc />
    public bool IsPaperCodeValid(ExamCode examCode, TrackCode? track, string paperCode)
    {
        if (string.IsNullOrWhiteSpace(paperCode)) return false;

        var snapshot = _catalog.Current;
        if (!snapshot.TargetsByCode.TryGetValue(examCode.Value, out var target))
        {
            // Unknown exam code — can't validate; be strict.
            return false;
        }

        // Defensive track cross-check. The catalog's TrackCode is the
        // authoritative pairing; if the caller's track doesn't match, the
        // request is malformed.
        if (!TrackMatches(target.Track, track)) return false;

        return target.MinistryQuestionPaperCodes.Contains(
            paperCode, StringComparer.Ordinal);
    }

    private static bool TrackMatches(string? catalogTrack, TrackCode? callerTrack)
    {
        if (catalogTrack is null && callerTrack is null) return true;
        if (catalogTrack is null || callerTrack is null) return false;
        return string.Equals(catalogTrack, callerTrack.Value.Value, StringComparison.Ordinal);
    }
}
