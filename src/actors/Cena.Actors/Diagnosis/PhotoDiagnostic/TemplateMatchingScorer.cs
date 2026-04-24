// =============================================================================
// Cena Platform — TemplateMatchingScorer (EPIC-PRR-J PRR-374)
//
// Given a CAS-detected break signature, scores each candidate misconception
// template from the closed taxonomy. Returns the best-fit template if its
// score exceeds MinConfidence; otherwise null (caller shows the
// conservative "let me check with your teacher" fallback).
//
// v1 scoring heuristic: exact break-type match → 1.0; no match → 0.0.
// A richer LaTeX-similarity scorer can slot in behind the same interface.
// =============================================================================

namespace Cena.Actors.Diagnosis.PhotoDiagnostic;

/// <summary>Input signature extracted from a CAS-verifier failure.</summary>
public sealed record CasBreakSignature(
    MisconceptionBreakType BreakType,
    string StudentExpression,
    string ExpectedExpression,
    string ContextLatex);

/// <summary>Scored template match result.</summary>
public sealed record TemplateMatch(
    MisconceptionTemplate Template,
    double Score);

/// <summary>Scorer port.</summary>
public interface ITemplateMatchingScorer
{
    /// <summary>
    /// Pick the best-fit template for the given break. Returns null when no
    /// template exceeds its MinConfidence.
    /// </summary>
    TemplateMatch? PickBestMatch(CasBreakSignature signature);
}

/// <summary>Default scorer — exact break-type match, no LaTeX similarity yet.</summary>
public sealed class TemplateMatchingScorer : ITemplateMatchingScorer
{
    /// <inheritdoc/>
    public TemplateMatch? PickBestMatch(CasBreakSignature signature)
    {
        ArgumentNullException.ThrowIfNull(signature);
        var candidates = BagrutMath4MisconceptionTaxonomy
            .FindCandidates(signature.BreakType)
            .ToList();
        if (candidates.Count == 0) return null;
        var best = candidates
            .Select(t => new TemplateMatch(t, 1.0))
            .Where(m => m.Score >= m.Template.MinConfidence)
            .OrderByDescending(m => m.Score)
            .FirstOrDefault();
        return best;
    }
}
