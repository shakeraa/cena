// =============================================================================
// Cena Platform — SymPy Template Guard (prr-010, Layer 2)
//
// Parse-side guards that run BEFORE a CAS verification request is marshalled
// to the SymPy sidecar. LLM-generated templates are untrusted input; these
// guards are the first layer of defense (the Python sidecar re-applies a
// matching whitelist as defense-in-depth — see docker/sympy-sidecar/sympy_worker.py).
//
// Rationale (pre-release-review 2026-04-20):
//   Before prr-010, SymPySidecarClient.cs:103 only enforced a millisecond
//   timeout on the NATS request. Nothing stopped an LLM-authored expression
//   from containing dunder-chain escapes, injected `import` statements,
//   direct `exec`/`eval`, or a reference to `sympy.printing.preview` (SSRF
//   via LaTeX-to-PNG conversion). The CAS engine is the correctness oracle
//   (ADR-0002); the sandbox is containment. Both are required.
//
// Guard contract:
//   * Any banned token (case-sensitive substring) in the raw expression →
//     reject with a structured reason. No parsing is attempted.
//   * Empty / whitespace / null expression → reject.
//   * Expression > MaxExpressionLength → reject (pre-empts memory bombs
//     before they reach the sidecar NATS buffer).
//
// Fail-closed philosophy: when in doubt, reject. A false-positive means one
// LLM-authored template is rewritten; a false-negative means the sidecar
// process is compromised.
// =============================================================================

namespace Cena.Actors.Cas;

/// <summary>
/// Result of screening an untrusted SymPy template. <see cref="Allowed"/>
/// templates are safe to marshal to the sidecar; rejected templates carry
/// a human-readable <see cref="Reason"/> for logs / admin review queue.
/// </summary>
public readonly record struct SymPyGuardResult(bool Allowed, string? Reason, string? BannedToken)
{
    public static SymPyGuardResult Ok() => new(true, null, null);
    public static SymPyGuardResult Reject(string reason, string? bannedToken = null) =>
        new(false, reason, bannedToken);
}

/// <summary>
/// Screens LLM-authored SymPy expressions before they reach the sidecar.
/// Thread-safe and allocation-free on the happy path (no regex, plain
/// substring scan).
/// </summary>
public interface ISymPyTemplateGuard
{
    /// <summary>
    /// Screen a single expression.
    /// </summary>
    SymPyGuardResult Screen(string? expression);

    /// <summary>
    /// Screen every expression in a <see cref="CasVerifyRequest"/>. Returns
    /// the first failing screen, or <see cref="SymPyGuardResult.Ok"/> if all
    /// expressions pass.
    /// </summary>
    SymPyGuardResult Screen(CasVerifyRequest request);
}

/// <summary>
/// Default guard. The banned-token list is deliberately narrow and explicit
/// — we are looking for the known sandbox-escape and SSRF primitives that
/// apply to a SymPy parse context, not trying to be a general Python linter.
/// </summary>
public sealed class SymPyTemplateGuard : ISymPyTemplateGuard
{
    /// <summary>
    /// Hard cap on expression length. Chosen to accommodate realistic
    /// school-level algebra / calculus expressions (&lt; 500 chars typical)
    /// while refusing obvious memory bombs like huge-power chains which
    /// exceed a few hundred characters only when deliberately padded.
    /// </summary>
    public const int MaxExpressionLength = 2048;

    /// <summary>
    /// Banned tokens. Each entry represents a distinct escape/SSRF primitive:
    ///
    ///   * `__`        — blocks subclasses / bases / import / globals / class
    ///                   escape chains built from dunder attributes. No
    ///                   legitimate SymPy expression we accept contains a dunder.
    ///   * `import`    — blocks injected import statements regardless of
    ///                   dunder wrapping.
    ///   * `exec` / `eval` — blocks direct code execution.
    ///   * `compile`   — blocks `compile(...)` which can feed `exec`.
    ///   * `lambda`    — blocks lambda injection paths into `sympify`.
    ///   * `open(`     — blocks filesystem access.
    ///   * `os.` / `sys.` / `subprocess` — blocks process / interpreter
    ///                   introspection and shell invocation vectors.
    ///   * `printing.preview` / `preview(` — blocks the SymPy SSRF primitive
    ///                   (LaTeX-to-PNG via an external call — known vector
    ///                   per AXIS_8_Content_Authoring_Quality_Research.md).
    ///   * `globals(` / `locals(` — blocks namespace introspection.
    ///   * `getattr(`  — blocks indirect attribute access often used in
    ///                   escape chains.
    ///   * `file(`     — blocks legacy Python 2 file handle (harmless in
    ///                   Python 3 but cheap to include).
    /// </summary>
    /// <remarks>
    /// Case-sensitive match against the raw expression (Python is case-sensitive).
    /// A whitespace-normalized copy of the input is ALSO scanned so that
    /// spacing-based bypasses like `__ subclasses __` are caught. The
    /// detection layer is intentionally narrow; the sidecar's own whitelist
    /// is the second wall.
    /// </remarks>
    private static readonly string[] BannedTokens =
    {
        "__",
        "import",
        "exec",
        "eval",
        "compile",
        "lambda",
        "open(",
        "os.",
        "sys.",
        "subprocess",
        "preview(",
        "printing.preview",
        "globals(",
        "locals(",
        "getattr(",
        "file(",
    };

    public SymPyGuardResult Screen(string? expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
            return SymPyGuardResult.Reject("expression is empty or whitespace");

        if (expression.Length > MaxExpressionLength)
            return SymPyGuardResult.Reject(
                $"expression exceeds max length {MaxExpressionLength} (got {expression.Length})");

        // Raw scan — catches the common shape.
        foreach (var token in BannedTokens)
        {
            if (expression.Contains(token, StringComparison.Ordinal))
                return SymPyGuardResult.Reject(
                    $"expression contains banned token '{token}'", token);
        }

        // Whitespace-normalised scan — catches spacing-based bypasses where
        // the attacker inserts whitespace to avoid substring match. We only
        // allocate on paths that actually contained whitespace; otherwise we
        // reuse the already-scanned original string reference.
        var normalised = StripWhitespace(expression);
        if (!ReferenceEquals(normalised, expression))
        {
            foreach (var token in BannedTokens)
            {
                if (normalised.Contains(token, StringComparison.Ordinal))
                    return SymPyGuardResult.Reject(
                        $"expression contains banned token '{token}' after whitespace normalisation",
                        token);
            }
        }

        return SymPyGuardResult.Ok();
    }

    public SymPyGuardResult Screen(CasVerifyRequest request)
    {
        if (request is null) return SymPyGuardResult.Reject("request is null");

        var a = Screen(request.ExpressionA);
        if (!a.Allowed) return a;

        if (!string.IsNullOrEmpty(request.ExpressionB))
        {
            var b = Screen(request.ExpressionB);
            if (!b.Allowed) return b;
        }

        if (!string.IsNullOrEmpty(request.Variable))
        {
            var v = Screen(request.Variable);
            if (!v.Allowed) return v;
        }

        return SymPyGuardResult.Ok();
    }

    /// <summary>
    /// Strip ALL whitespace (space, tab, newline, carriage return) from the
    /// expression so that attackers cannot hide a banned token by inserting
    /// whitespace. Returns the original reference if no whitespace was
    /// present (to avoid re-scanning an identical string).
    /// </summary>
    private static string StripWhitespace(string s)
    {
        var hasWs = false;
        for (var i = 0; i < s.Length; i++)
        {
            if (char.IsWhiteSpace(s[i])) { hasWs = true; break; }
        }
        if (!hasWs) return s;

        var buf = new char[s.Length];
        var j = 0;
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (!char.IsWhiteSpace(c)) buf[j++] = c;
        }
        return new string(buf, 0, j);
    }
}
