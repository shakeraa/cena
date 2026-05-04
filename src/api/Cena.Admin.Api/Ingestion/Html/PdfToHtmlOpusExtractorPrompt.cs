// =============================================================================
// Cena Platform — PdfToHtmlOpusExtractorPrompt (2026-05-04, t_1c57e7389cb4)
//
// Static container for the system prompt + reference SVG anchors used by
// PdfToHtmlOpusExtractor. Lives in a SEPARATE file from the extractor so
// the architecture-test scanner (EveryLlmServiceEmitsTraceIdTest +
// CostMetricEmittedTest) — which uses single-quote-aware string-stripping
// that does NOT understand C# raw-string `"""..."""` syntax — can see
// the IActivityPropagator / GetTraceId( / ILlmCostMetric.Record(
// references in the extractor without triple-quoted SVG content tripping
// the scanner's `inStr` toggle. Mirrors the
// LlmBagrutQuestionSegmenterPrompt.cs pattern.
//
// Why this matters: the architecture tests are CI gates. Folding the SVG
// content into the same file as the [TaskRouting] class means every triple-
// quote `"` flips the scanner's state, masking the trace_id + cost-metric
// tokens at the bottom of the file — the gate fails despite the production
// class doing exactly what it should.
//
// All content here is the user's verbatim recipe (Q4_INSCRIBED_TRIANGLE_SVG
// + Q5_KITE_WITH_PARALLEL_SVG) plus the figure-convention rubric the
// 2026-05-04 coordinator upgrade added. Do not modify these without
// re-validating against claude.ai's gold-standard output on the corpus —
// the SVGs are LOAD-BEARING for figure quality on questions structurally
// similar to Q4 (inscribed triangle) / Q5 (kite with parallel).
// =============================================================================

namespace Cena.Admin.Api.Ingestion.Html;

internal static class PdfToHtmlOpusExtractorPrompt
{
    /// <summary>
    /// Reference SVG for an inscribed-triangle figure (Q4-shape). Lifted
    /// verbatim from the user's gold-standard recipe — every coordinate is
    /// preserved.
    /// </summary>
    internal const string Q4InscribedTriangleSvg =
        """
        <svg width="320" height="280" viewBox="0 0 320 280" xmlns="http://www.w3.org/2000/svg">
          <circle cx="140" cy="148" r="93" fill="none" stroke="black" stroke-width="1.2"/>
          <polygon points="60,100 170,60 160,240" fill="none" stroke="black" stroke-width="1.2"/>
          <line x1="170" y1="60" x2="280" y2="20" stroke="black" stroke-width="1.2"/>
          <line x1="280" y1="20" x2="228" y2="116" stroke="black" stroke-width="1.2"/>
          <line x1="60" y1="100" x2="228" y2="116" stroke="black" stroke-width="1.2"/>
          <line x1="60" y1="100" x2="160" y2="240" stroke="black" stroke-width="1.2"/>
          <circle cx="60" cy="100" r="2.2" fill="black"/>
          <circle cx="170" cy="60" r="2.2" fill="black"/>
          <circle cx="280" cy="20" r="2.2" fill="black"/>
          <circle cx="228" cy="116" r="2.2" fill="black"/>
          <circle cx="160" cy="240" r="2.2" fill="black"/>
          <text x="42" y="98" font-style="italic" font-family="serif" font-size="14">A</text>
          <text x="166" y="52" font-style="italic" font-family="serif" font-size="14">B</text>
          <text x="287" y="20" font-style="italic" font-family="serif" font-size="14">K</text>
          <text x="234" y="120" font-style="italic" font-family="serif" font-size="14">C</text>
          <text x="155" y="258" font-style="italic" font-family="serif" font-size="14">F</text>
        </svg>
        """;

    /// <summary>
    /// Reference SVG for a kite-with-parallel-construction-line figure
    /// (Q5-shape). Lifted verbatim from the user's gold-standard recipe;
    /// dashed construction lines (AE, DE) MUST stay dashed when the model
    /// reuses this layout.
    /// </summary>
    internal const string Q5KiteWithParallelSvg =
        """
        <svg width="280" height="260" viewBox="0 0 280 260" xmlns="http://www.w3.org/2000/svg">
          <polygon points="100,25 35,105 100,220 165,105" fill="none" stroke="black" stroke-width="1.2"/>
          <line x1="35" y1="105" x2="165" y2="105" stroke="black" stroke-width="1.2"/>
          <line x1="100" y1="220" x2="255" y2="220" stroke="black" stroke-width="1.2" stroke-dasharray="3,3"/>
          <line x1="165" y1="105" x2="255" y2="220" stroke="black" stroke-width="1.2" stroke-dasharray="3,3"/>
          <circle cx="100" cy="25" r="2.2" fill="black"/>
          <circle cx="35" cy="105" r="2.2" fill="black"/>
          <circle cx="165" cy="105" r="2.2" fill="black"/>
          <circle cx="100" cy="220" r="2.2" fill="black"/>
          <circle cx="255" cy="220" r="2.2" fill="black"/>
          <text x="95" y="18" font-style="italic" font-family="serif" font-size="14">C</text>
          <text x="20" y="110" font-style="italic" font-family="serif" font-size="14">B</text>
          <text x="170" y="110" font-style="italic" font-family="serif" font-size="14">D</text>
          <text x="95" y="240" font-style="italic" font-family="serif" font-size="14">A</text>
          <text x="263" y="225" font-style="italic" font-family="serif" font-size="14">E</text>
        </svg>
        """;

    /// <summary>
    /// System prompt — adapted from the user's validated recipe and the
    /// 2026-05-04 coordinator upgrade that adds reference-SVG anchors. The
    /// base rubric is VERBATIM from the user's first-pass recipe (preserves
    /// the gold-standard output on corpus/tests/35581-q.pdf); the SVG figure
    /// conventions + EXAMPLE blocks come from the directive's upgrade.
    ///
    /// Do NOT tighten or loosen this rubric without re-validating against
    /// claude.ai's gold-standard output on the test corpus.
    /// </summary>
    internal static readonly string SystemPrompt = $$"""
        You convert exam-style PDFs into a single self-contained HTML document.

        Rules:
        - Output ONLY the HTML (no markdown fences, no commentary).
        - Preserve the original language and reading direction. For Hebrew/Arabic
          set <html dir="rtl"> and lang accordingly.
        - Keep the visual structure: question numbering, sub-question letters,
          paragraph breaks.
        - Render math inline using HTML (sup, sub, fraction divs). Do NOT use LaTeX.
        - Recreate every figure as inline <svg> with labeled points/lines that match
          the original layout faithfully (positions, labels, dashed vs solid).
        - Style with a small embedded <style> block: serif font, max-width container,
          subtle borders. No external CSS or JS.

        SVG figure conventions:
        - viewBox should leave roughly 20px of padding around the geometry so labels
          do not clip at the edges.
        - Use stroke="black" stroke-width="1.2" for every primary line and curve.
        - Mark every named point with a filled black circle at r="2.2".
        - Place point labels in italic serif (font-style="italic" font-family="serif"
          font-size="14") with a 5–8px offset from the point so the dot stays
          visible.
        - Construction lines (auxiliaries, parallels) use stroke-dasharray="3,3";
          primary geometry stays solid.
        - Circles use fill="none" so inscribed shapes show through.
        - Solve the geometric constraints described in the question BEFORE placing
          coordinates — pick coordinates that actually satisfy "tangent at A",
          "midpoint of BC", "parallel to AC", etc., rather than approximating.

        EXAMPLE — inscribed triangle ABF in a circle with chord BK and intersection
        C: structurally similar Bagrut figures should reuse these proportions,
        label positions, and the chord-extending construction line shape.
        {{Q4InscribedTriangleSvg}}

        EXAMPLE — kite ABCD with diagonal BD and parallel construction line AE
        (dashed) intersecting an extension at E: structurally similar kite/quadri-
        lateral figures with auxiliary construction should reuse the dashed line
        convention and label-offset pattern.
        {{Q5KiteWithParallelSvg}}

        When you encounter a figure structurally similar to either reference,
        reuse its proportions and label conventions; only adjust coordinates for
        the specific constraints of the new figure.
        """;
}
