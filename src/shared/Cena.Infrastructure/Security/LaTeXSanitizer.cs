// =============================================================================
// Cena Platform — LaTeX Sanitization (LATEX-001)
//
// 200-command allowlist for LaTeX rendering. Blocks CVE-2024-28243 and
// other LaTeX injection vectors. All math content passes through this
// before KaTeX rendering.
// =============================================================================

namespace Cena.Infrastructure.Security;

/// <summary>
/// Sanitizes LaTeX input against a strict allowlist to prevent injection attacks.
/// CVE-2024-28243: MathJax/KaTeX command injection via crafted LaTeX.
/// </summary>
public static class LaTeXSanitizer
{
    /// <summary>
    /// Allowlisted LaTeX commands. Any command not in this set is stripped.
    /// </summary>
    private static readonly HashSet<string> AllowedCommands = new(StringComparer.Ordinal)
    {
        // Arithmetic & algebra
        "frac", "dfrac", "tfrac", "sqrt", "root", "pm", "mp", "times", "div", "cdot",
        "ldots", "cdots", "vdots", "ddots", "dots",

        // Relations
        "eq", "neq", "ne", "lt", "gt", "le", "ge", "leq", "geq", "approx", "sim",
        "equiv", "cong", "propto", "ll", "gg",

        // Greek letters
        "alpha", "beta", "gamma", "delta", "epsilon", "varepsilon", "zeta", "eta",
        "theta", "vartheta", "iota", "kappa", "lambda", "mu", "nu", "xi", "pi",
        "varpi", "rho", "varrho", "sigma", "varsigma", "tau", "upsilon", "phi",
        "varphi", "chi", "psi", "omega",
        "Gamma", "Delta", "Theta", "Lambda", "Xi", "Pi", "Sigma", "Upsilon",
        "Phi", "Psi", "Omega",

        // Trigonometric
        "sin", "cos", "tan", "cot", "sec", "csc",
        "arcsin", "arccos", "arctan", "sinh", "cosh", "tanh",

        // Calculus
        "lim", "int", "iint", "iiint", "oint", "sum", "prod",
        "partial", "nabla", "infty", "to", "rightarrow", "leftarrow",
        "Rightarrow", "Leftarrow", "Leftrightarrow",

        // Formatting
        "text", "textbf", "textit", "mathrm", "mathbf", "mathit", "mathsf",
        "mathcal", "mathbb", "mathfrak", "boldsymbol",
        "left", "right", "big", "Big", "bigg", "Bigg",
        "overline", "underline", "hat", "bar", "vec", "dot", "ddot", "tilde",
        "widehat", "widetilde", "overrightarrow",

        // Spacing
        "quad", "qquad", "hspace", "vspace", "kern", "mkern",
        ",", ";", "!", ":", " ",

        // Brackets & delimiters
        "langle", "rangle", "lbrace", "rbrace", "lceil", "rceil",
        "lfloor", "rfloor", "lvert", "rvert", "lVert", "rVert",

        // Matrices
        "begin", "end", "matrix", "pmatrix", "bmatrix", "vmatrix", "Vmatrix",
        "cases", "aligned", "align", "array",

        // Misc math
        "log", "ln", "exp", "det", "dim", "ker", "deg", "gcd", "min", "max",
        "sup", "inf", "arg", "mod", "bmod", "pmod",
        "forall", "exists", "in", "notin", "subset", "supset", "subseteq", "supseteq",
        "cup", "cap", "setminus", "emptyset", "varnothing",
        "neg", "land", "lor", "implies", "iff",
        "angle", "triangle", "square", "circle", "parallel", "perp",
        "therefore", "because",
        "boxed", "cancel", "bcancel", "xcancel",

        // Units (physics)
        "unit", "si", "SI", "metre", "kilogram", "second", "ampere", "kelvin",
    };

    /// <summary>
    /// Explicitly banned commands (security risk even if they look harmless).
    /// </summary>
    private static readonly HashSet<string> BannedCommands = new(StringComparer.Ordinal)
    {
        // File system access (CVE-2024-28243 vectors)
        "input", "include", "includegraphics", "usepackage", "documentclass",
        "write", "read", "openin", "openout", "closein", "closeout",
        "immediate", "newwrite", "newread",

        // Code execution
        "url", "href", "hyperref", "verb", "lstinline", "mint",
        "catcode", "def", "edef", "gdef", "xdef", "let", "futurelet",
        "expandafter", "csname", "endcsname", "relax",
        "newcommand", "renewcommand", "providecommand", "DeclareMathOperator",

        // Shell access
        "write18", "ShellEscape", "directlua", "luadirect",
    };

    /// <summary>
    /// Sanitize LaTeX input by stripping commands not in the allowlist.
    /// Returns safe LaTeX or null if the input is entirely malicious.
    /// </summary>
    public static string Sanitize(string latex)
    {
        if (string.IsNullOrWhiteSpace(latex)) return latex;

        var result = new System.Text.StringBuilder(latex.Length);
        int i = 0;

        while (i < latex.Length)
        {
            if (latex[i] == '\\' && i + 1 < latex.Length && char.IsLetter(latex[i + 1]))
            {
                // Extract command name
                int start = i + 1;
                int end = start;
                while (end < latex.Length && char.IsLetter(latex[end])) end++;
                var command = latex[start..end];

                if (BannedCommands.Contains(command))
                {
                    // Strip banned command entirely (skip to next non-brace char)
                    i = end;
                    if (i < latex.Length && latex[i] == '{')
                    {
                        int depth = 1;
                        i++;
                        while (i < latex.Length && depth > 0)
                        {
                            if (latex[i] == '{') depth++;
                            else if (latex[i] == '}') depth--;
                            i++;
                        }
                    }
                    continue;
                }

                if (AllowedCommands.Contains(command))
                {
                    result.Append(latex[(start - 1)..end]); // include backslash
                }
                // else: unknown command — strip silently

                i = end;
            }
            else
            {
                result.Append(latex[i]);
                i++;
            }
        }

        return result.ToString();
    }

    /// <summary>
    /// Check if LaTeX contains any banned commands.
    /// </summary>
    public static bool ContainsBannedCommands(string latex)
    {
        if (string.IsNullOrWhiteSpace(latex)) return false;

        foreach (var banned in BannedCommands)
        {
            if (latex.Contains($"\\{banned}", StringComparison.Ordinal))
                return true;
        }
        return false;
    }
}
