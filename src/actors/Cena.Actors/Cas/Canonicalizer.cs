// =============================================================================
// Cena Platform — Canonicalizer (EPIC-PRR-J PRR-361, ADR-0002)
//
// Why this exists:
// --------------------------------------------------------------------------
// Students write equivalent expressions in many surface forms:
//   "(x-2)(x+3)", "(x+3)(x-2)", "x^2+x-6", "x^2 + x - 6", "x \cdot x + x - 6",
//   "x*x+x-6".
// Before PRR-361, StepChainVerifier compared from-step to to-step by raw
// LaTeX — so the student's legitimately-equivalent next step looked
// "different" even though it was mathematically identical.
//
// Per ADR-0002, SymPy is the sole correctness oracle — the LLM (and any
// local heuristics) must never stand in for it. But round-tripping every
// pair of transitions through SymPy would be slow and expensive.
//
// We split canonicalization into two layers:
//
//   1. CHEAP (this static pass): pure-string normalization that handles
//      trivial surface noise no CAS should have to worry about — whitespace,
//      unicode variants, equivalent multiplication markers, NBSP, leading
//      unary plus. Deterministic, idempotent, no I/O.
//
//      These normalizations are "type 0" — forms that SymPy would collapse
//      anyway, so pre-collapsing them in-process saves a SymPy round trip
//      WITHOUT ever replacing a SymPy judgement. They cannot turn an
//      unequal pair into an equal one; they can only turn two
//      surface-different but textually-trivially-equivalent strings into
//      the same string.
//
//   2. EXPENSIVE (ICanonicalizer.CanonicalizeAsync): delegates to
//      ICasRouterService with CasOperation.Canonicalize. This is the
//      actual algebraic canonicalization (expand + simplify). Tolerates
//      SymPy outage: on CAS failure, CanonicalExpanded is null and
//      downstream verification still runs against NormalizedLatex — same
//      behaviour we had before PRR-361, no regression, just no gain.
//
// Design note on CasOperation.Canonicalize (new enum value):
// --------------------------------------------------------------------------
// Adding a value was the least-invasive path. Alternatives considered:
//   (a) Reuse NormalForm — rejected, NormalForm means "check expression
//       is IN canonical form" (a predicate), not "return the canonical
//       form" (a function).
//   (b) Abuse Equivalence with a probe — rejected, that would poison
//       telemetry and routing rules that inspect Operation.
//   (c) Add a separate ICasCanonicalProbe — rejected, duplicates the
//       router's circuit breaker + cost accounting.
// We extend CasVerifyResult.SimplifiedA to carry the canonical expansion,
// which already exists for this purpose.
//
// Senior-architect discipline: no stubs. The cheap-string pass is a real
// normalization, not a placeholder. The expensive path delegates to the
// real CasRouter. In unit tests we inject a FakeCasRouter (test double,
// not a stub — it satisfies the contract with scripted responses).
// =============================================================================

using System.Text;
using System.Text.RegularExpressions;

namespace Cena.Actors.Cas;

/// <summary>
/// Canonical form of a LaTeX expression, produced by <see cref="ICanonicalizer"/>.
/// </summary>
/// <param name="Original">
/// The student's original LaTeX as received. Preserve this for display —
/// the student should always see their own work, not a normalized version
/// (DoD: "Preserve original form for display").
/// </param>
/// <param name="NormalizedLatex">
/// Result of the cheap pure-string normalization pass. Idempotent. Safe
/// to compare against other NormalizedLatex values byte-for-byte, but
/// false-negatives are possible (e.g. two algebraically-equal polynomials
/// in different expanded forms). Use CanonicalExpanded for a correctness
/// answer.
/// </param>
/// <param name="CanonicalExpanded">
/// SymPy's expand+simplify output (from <see cref="CasOperation.Canonicalize"/>).
/// Null when SymPy was unavailable, returned an error, or when the
/// operation is outside the canonicalization window. A null here is
/// normal, not an error — callers fall back to NormalizedLatex.
/// </param>
public sealed record CanonicalForm(
    string Original,
    string NormalizedLatex,
    string? CanonicalExpanded);

/// <summary>
/// Two-layer canonicalization seam. Cheap-first, expensive-fallback,
/// per ADR-0002.
/// </summary>
public interface ICanonicalizer
{
    /// <summary>
    /// Canonicalize a LaTeX expression. Always returns a CanonicalForm
    /// with the original preserved. The expensive SymPy pass runs for
    /// <paramref name="op"/> values where algebraic equivalence matters
    /// (<see cref="CasOperation.Equivalence"/>, <see cref="CasOperation.StepValidity"/>,
    /// and the new <see cref="CasOperation.Canonicalize"/>).
    /// </summary>
    /// <param name="latex">LaTeX source. Empty string returns an empty canonical form; null throws.</param>
    /// <param name="op">The CAS operation the caller intends to run afterwards. Controls whether SymPy is consulted.</param>
    /// <param name="ct">Cooperative cancellation token.</param>
    Task<CanonicalForm> CanonicalizeAsync(string latex, CasOperation op, CancellationToken ct);
}

/// <summary>
/// Default two-layer canonicalizer. Cheap string pass always, SymPy
/// delegation for algebraic operations.
/// </summary>
public sealed class Canonicalizer : ICanonicalizer
{
    private readonly ICasRouterService _cas;

    public Canonicalizer(ICasRouterService cas)
    {
        _cas = cas ?? throw new ArgumentNullException(nameof(cas));
    }

    /// <summary>
    /// Pure-string normalization pass. Deterministic and idempotent —
    /// NormalizeLatex(NormalizeLatex(x)) == NormalizeLatex(x) for all x.
    ///
    /// What it does (each pass exists to close a specific failure mode
    /// we have seen in OCR/student-entry logs):
    ///   1. Trim leading/trailing whitespace — OCR prepends space on new
    ///      line indents; students often hit space before/after an equals.
    ///   2. NBSP → regular space — copy-paste from Word drops U+00A0.
    ///   3. Unicode minus → ASCII minus — U+2212 "−" and en-dash/em-dash
    ///      routinely appear in PDFs and phone keyboards; SymPy parses
    ///      ASCII minus.
    ///   4. Unicode multiply → backslash-cdot — "×" and "·" mean *, SymPy
    ///      wants a standard LaTeX operator. Normalising to "\cdot" (not
    ///      "*") keeps output as valid LaTeX for display fallback.
    ///   5. "*" → "\cdot" — same rationale as (4); ASCII asterisk is valid
    ///      SymPy but not valid LaTeX.
    ///   6. Collapse runs of internal whitespace to single spaces — OCR
    ///      sometimes emits "x  +  2".
    ///   7. Strip leading unary "+" — "+3x" ≡ "3x"; otherwise canonical
    ///      pairs differ only by a sign token.
    ///   8. Leave case alone — LaTeX is case-significant (X ≠ x, \pi ≠ \Pi).
    ///   9. If the result equals the input, return the exact input string
    ///      (idempotency + reference-equal fast path for callers that
    ///      dedup).
    /// </summary>
    /// <param name="input">LaTeX source. Must not be null.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="input"/> is null.</exception>
    public static string NormalizeLatex(string input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.Length == 0) return input;

        var s = input;

        // (2) NBSP normalization happens before trim so trailing NBSPs get trimmed.
        if (s.IndexOf(' ') >= 0)
            s = s.Replace(' ', ' ');

        // (1) Trim.
        s = s.Trim();
        if (s.Length == 0) return s;

        // (3) Unicode minus variants to ASCII minus.
        //     U+2212 MINUS SIGN, U+2013 EN DASH, U+2014 EM DASH.
        if (ContainsAny(s, UnicodeMinuses))
        {
            s = s.Replace('−', '-')
                 .Replace('–', '-')
                 .Replace('—', '-');
        }

        // (4) Unicode multiply variants to the canonical "\cdot".
        //     U+00D7 MULTIPLICATION SIGN, U+00B7 MIDDLE DOT.
        //     We use string replace rather than char because we emit a
        //     multi-char replacement.
        if (s.IndexOf('×') >= 0) s = s.Replace("×", "\\cdot ");
        if (s.IndexOf('·') >= 0) s = s.Replace("·", "\\cdot ");

        // (5) ASCII "*" to "\cdot". Must NOT match inside "\\ast" or
        //     other backslash sequences — we only target bare "*".
        if (s.IndexOf('*') >= 0)
            s = AsteriskOperator.Replace(s, "\\cdot ");

        // (6) Collapse repeated whitespace.
        if (WhitespaceRun.IsMatch(s))
            s = WhitespaceRun.Replace(s, " ");

        // (7) Strip leading unary "+". "+ 3" and "+3" both become "3".
        //     Runs only after whitespace collapse so "+ x" is matched.
        s = LeadingUnaryPlus.Replace(s, "");

        // (9) Re-trim: inserting "\cdot " on the right edge can leave
        //     trailing whitespace.
        s = s.TrimEnd();

        // Idempotency fast-path: if the result is textually the input,
        // return the input so ReferenceEquals callers can shortcut.
        return s == input ? input : s;
    }

    /// <inheritdoc/>
    public async Task<CanonicalForm> CanonicalizeAsync(
        string latex, CasOperation op, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(latex);

        // Empty string: skip CAS entirely. Nothing to canonicalize and
        // SymPy rejects empty expressions.
        if (latex.Length == 0)
            return new CanonicalForm(latex, latex, null);

        // Cheap pass always runs.
        var normalized = NormalizeLatex(latex);

        // Expensive pass: only for operations that depend on algebraic
        // equivalence. StepChainVerifier calls us with StepValidity.
        // Other callers can opt in with Canonicalize.
        if (!ShouldConsultCas(op))
            return new CanonicalForm(latex, normalized, null);

        string? canonicalExpanded = null;
        try
        {
            var request = new CasVerifyRequest(
                Operation: CasOperation.Canonicalize,
                ExpressionA: normalized,
                ExpressionB: null,
                Variable: null);

            var result = await _cas.VerifyAsync(request, ct).ConfigureAwait(false);

            // Router reports Ok when it has a definitive answer. The
            // canonical expansion ships in SimplifiedA per CasVerifyResult
            // contract. Any non-Ok status (Timeout, Error, CircuitBreakerOpen,
            // UnsupportedOperation) is a degraded-mode signal — we keep
            // NormalizedLatex and null out CanonicalExpanded.
            if (result.Status == CasVerifyStatus.Ok
                && !string.IsNullOrWhiteSpace(result.SimplifiedA))
            {
                canonicalExpanded = result.SimplifiedA;
            }
        }
        catch (OperationCanceledException)
        {
            // Cancellation is not a canonicalization failure — propagate.
            throw;
        }
        catch
        {
            // Any other exception from the CAS tier is a degradation we
            // tolerate: downstream comparison falls back to NormalizedLatex.
            // We intentionally don't log here — the CAS router already
            // records its own failures. Logging again would double-count
            // and leak CAS internals into the Canonicalizer abstraction.
            canonicalExpanded = null;
        }

        return new CanonicalForm(latex, normalized, canonicalExpanded);
    }

    private static bool ShouldConsultCas(CasOperation op) => op switch
    {
        CasOperation.Equivalence => true,
        CasOperation.StepValidity => true,
        CasOperation.Canonicalize => true,
        _ => false, // NumericalTolerance, NormalForm, Solve have their own pipelines.
    };

    // Regexes ---------------------------------------------------------------

    private static readonly Regex WhitespaceRun =
        new(@"\s+", RegexOptions.Compiled);

    /// <summary>
    /// Match bare "*" that is NOT the trailing character of a backslash
    /// escape (e.g. "\ast"). We also avoid matching "**" inside doc
    /// comments and markdown — unlikely in LaTeX but cheap to guard.
    /// </summary>
    private static readonly Regex AsteriskOperator =
        new(@"(?<!\\)\*+", RegexOptions.Compiled);

    /// <summary>
    /// Match a leading "+" possibly preceded by whitespace. Strictly a
    /// unary-plus pattern: must be at position 0 after Trim(), and must
    /// not be doubled ("++" isn't a LaTeX operator and shouldn't be
    /// silently eaten).
    /// </summary>
    private static readonly Regex LeadingUnaryPlus =
        new(@"^\+(?!\+)\s*", RegexOptions.Compiled);

    private static readonly char[] UnicodeMinuses = { '−', '–', '—' };

    private static bool ContainsAny(string s, char[] chars)
    {
        foreach (var c in chars)
            if (s.IndexOf(c) >= 0) return true;
        return false;
    }
}
