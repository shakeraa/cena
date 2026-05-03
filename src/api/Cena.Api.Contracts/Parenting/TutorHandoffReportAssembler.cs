// =============================================================================
// Cena Platform — TutorHandoffReportAssembler (EPIC-PRR-I PRR-325)
//
// Why this exists:
//   The tutor-handoff report has four policy lines that must hold on every
//   response regardless of caller:
//     1. Opt-in flags are authoritative. If the parent says
//        IncludeMisconceptions=false, the MisconceptionSummary field must
//        be null on the output — the endpoint layer must not be able to
//        "forget" this and leak the summary anyway.
//     2. TopicsPracticed order is preserved from the input (display order
//        carries recency / emphasis meaning).
//     3. The output window matches the request window exactly (after the
//        endpoint has normalised a null WindowStart into WindowEnd-30d);
//        the assembler never re-clamps.
//     4. GeneratedAtUtc is the wall clock at assemble time — never the
//        request's WindowEnd (those are two different semantics: "when
//        the report was made" vs "what window the report covers").
//
//   Rather than scatter these four rules across the endpoint layer where
//   they'd be one-shot and easily skipped by a refactor, the assembler
//   is a pure static function. The endpoint calls Assemble(request,
//   cards, now) and gets back a DTO that is guaranteed to satisfy the
//   four invariants. Locked by unit tests.
//
//   Matches the HouseholdDashboardAggregator pattern (EPIC-PRR-I PRR-324).
//
// Why here (Cena.Api.Contracts) and not in Cena.Actors:
//   Cena.Api.Contracts already depends on Cena.Actors for enum/state
//   types. The reverse would be cyclic — Cena.Actors cannot reference
//   wire-format DTOs. Since the assembler signature takes request + card
//   bundle and returns the response DTO (all wire-format types), the
//   only compile-legal home is the Contracts assembly. The function is
//   still pure — no I/O, no clock (now is injected), no DI — which
//   preserves every property that made "put it next to the domain" the
//   tempting alternative. Unit tests live in Cena.Actors.Tests, which
//   references Contracts transitively via the Student host project.
//
// What the assembler is NOT:
//   - Not a privacy boundary. The DTO shape itself enforces "summary
//     scalars only" (there are no session-id fields on any DTO). The
//     assembler's job is opt-in enforcement + field-order preservation.
//   - Not a data source. Cards come pre-built from the endpoint via its
//     read-model seams (mastery store, minutes projection, etc.).
//   - Not async. No I/O, no clock outside the `now` parameter. Pure
//     value in → value out.
//
// Ship-gate discipline:
//   Banned-term scan — no streak / countdown / scarcity language in
//   field names, banner, or flow. The assembler produces an
//   informational artefact; it does not manufacture urgency.
// =============================================================================

namespace Cena.Api.Contracts.Parenting;

/// <summary>
/// Pure assembler that folds a <see cref="TutorHandoffReportRequestDto"/>
/// and a pre-built <see cref="TutorHandoffCards"/> bundle into a
/// <see cref="TutorHandoffReportDto"/>. See file banner for invariants.
/// </summary>
public static class TutorHandoffReportAssembler
{
    /// <summary>
    /// Default reporting window when the caller does not supply
    /// <see cref="TutorHandoffReportRequestDto.WindowStart"/>. 30 days
    /// matches the DoD's "default 30d" contract.
    /// </summary>
    public static readonly TimeSpan DefaultWindow = TimeSpan.FromDays(30);

    /// <summary>
    /// Canonical locales the assembler recognises. Any other string on the
    /// request is accepted without mutation (the renderer falls back to
    /// LTR + English-style formatting), but only these three are tested
    /// end-to-end with label localisation.
    /// </summary>
    private static readonly HashSet<string> SupportedLocales = new(
        StringComparer.OrdinalIgnoreCase)
    {
        "he", "ar", "en",
    };

    /// <summary>
    /// Assemble the tutor-handoff report.
    /// </summary>
    /// <param name="request">
    /// Parent's report request (student id, window, opt-in flags, locale).
    /// The endpoint is responsible for IDOR-guarding the student id and
    /// feature-fencing the caller BEFORE reaching the assembler.
    /// </param>
    /// <param name="cards">
    /// Pre-built per-student aggregate bundle from the endpoint's read-model
    /// seams. The assembler copies scalar fields through conditionally
    /// based on the request's Include* flags — it never queries any
    /// source itself.
    /// </param>
    /// <param name="generatedAtUtc">
    /// Wall-clock timestamp to stamp on the output. Separate parameter
    /// (not derived from request/cards) so tests can pin it.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="request"/> or <paramref name="cards"/> is null.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="request"/> has an empty student id, a blank locale,
    /// or a window where the normalised start is not strictly before end.
    /// </exception>
    public static TutorHandoffReportDto Assemble(
        TutorHandoffReportRequestDto request,
        TutorHandoffCards cards,
        DateTimeOffset generatedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(cards);

        if (string.IsNullOrWhiteSpace(request.StudentSubjectIdEncrypted))
        {
            throw new ArgumentException(
                "TutorHandoffReportRequestDto.StudentSubjectIdEncrypted must be non-empty. " +
                "The endpoint layer is expected to reject empty / forged ids before " +
                "reaching the assembler (IDOR guard).",
                nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.Locale))
        {
            throw new ArgumentException(
                "TutorHandoffReportRequestDto.Locale must be non-empty (one of 'he' | 'ar' | 'en').",
                nameof(request));
        }

        // Normalise the window. The endpoint may supply a null WindowStart
        // to mean "default to 30 days back from WindowEnd" per the DoD.
        // We do the fill here (not the endpoint) so every path through the
        // assembler sees a well-formed window — the endpoint does not have
        // to remember to default it.
        var windowStart = request.WindowStart ?? request.WindowEnd - DefaultWindow;
        if (windowStart >= request.WindowEnd)
        {
            throw new ArgumentException(
                $"TutorHandoffReportRequestDto.WindowStart ({windowStart:o}) must be strictly " +
                $"before WindowEnd ({request.WindowEnd:o}).",
                nameof(request));
        }

        // Opt-in enforcement. When a flag is false, the corresponding output
        // field is null/empty REGARDLESS of what cards provide. This is the
        // privacy-of-choice guarantee — the parent chose to exclude a
        // section, and the assembler enforces it before the renderer runs.
        var topicsPracticed = cards.TopicsPracticed ?? Array.Empty<string>();
        var recommendedFocus = cards.RecommendedFocusAreas ?? Array.Empty<string>();

        IReadOnlyDictionary<string, MasteryDelta> masteryDeltas =
            request.IncludeMastery
                ? cards.MasteryDeltas ?? new Dictionary<string, MasteryDelta>()
                : new Dictionary<string, MasteryDelta>();

        long? timeOnTask = request.IncludeTimeOnTask ? cards.TimeOnTaskMinutes : null;

        string? misconceptionSummary = request.IncludeMisconceptions
            ? cards.MisconceptionSummary
            : null;

        // Locale is preserved as-is (case-insensitive recognition only
        // affects the renderer; the DTO holds what the caller supplied so
        // downstream consumers see their own input echoed back).
        var locale = SupportedLocales.Contains(request.Locale)
            ? request.Locale
            : request.Locale; // identity for unrecognised; renderer LTR-default

        return new TutorHandoffReportDto(
            StudentSubjectIdEncrypted: request.StudentSubjectIdEncrypted,
            GeneratedAtUtc: generatedAtUtc,
            WindowStart: windowStart,
            WindowEnd: request.WindowEnd,
            Locale: locale,
            StudentDisplayName: cards.StudentDisplayName,
            TopicsPracticed: topicsPracticed,
            MasteryDeltas: masteryDeltas,
            TimeOnTaskMinutes: timeOnTask,
            MisconceptionSummary: misconceptionSummary,
            RecommendedFocusAreas: recommendedFocus);
    }
}
