// =============================================================================
// Cena Platform — Coverage Cell (prr-201)
//
// Canonical coordinate in the coverage matrix. Methodology is part of the
// identity (ADR-0040) — a Halabi cell cannot be "filled" by a Rabinovitch
// variant. Language is part of the cell so the curator task is actionable.
//
// The address string is a stable, URL-safe identifier derived from the six
// axes; used as the idempotency key for stage-3 curator enqueue and as the
// label value for telemetry.
// =============================================================================

using Cena.Actors.QuestionBank.Templates;

namespace Cena.Actors.QuestionBank.Coverage;

/// <summary>
/// A single cell in the coverage matrix. Identity is the combination of all
/// six axes — two cells differing only in methodology are different cells.
/// </summary>
public sealed record CoverageCell
{
    public required TemplateTrack Track { get; init; }
    public required string Subject { get; init; }
    public required string Topic { get; init; }
    public required TemplateDifficulty Difficulty { get; init; }
    public required TemplateMethodology Methodology { get; init; }

    /// <summary>
    /// multiple-choice / free-text / step-solver etc. Mirrors
    /// <see cref="Cena.Infrastructure.Documents.QuestionDocument.QuestionType"/>.
    /// </summary>
    public required string QuestionType { get; init; }

    public string Language { get; init; } = "en";

    /// <summary>
    /// Stable URL-safe address. Used as idempotency key for stage 3 and
    /// as the label for per-cell metrics.
    /// </summary>
    public string Address =>
        string.Join(
            '/',
            Track.ToString().ToLowerInvariant(),
            Slug(Subject),
            Slug(Topic),
            Difficulty.ToString().ToLowerInvariant(),
            Methodology.ToString().ToLowerInvariant(),
            Slug(QuestionType),
            Language);

    private static string Slug(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "_";
        var chars = s.Trim().ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '-')
            .ToArray();
        var raw = new string(chars);
        // Collapse consecutive dashes.
        while (raw.Contains("--", StringComparison.Ordinal))
            raw = raw.Replace("--", "-", StringComparison.Ordinal);
        return raw.Trim('-');
    }
}
