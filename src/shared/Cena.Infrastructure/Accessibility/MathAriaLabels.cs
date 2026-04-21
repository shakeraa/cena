// =============================================================================
// Cena Platform — Screen-reader aria-labels for math (A11Y-SRE-001, PRR-031)
//
// Screen-reader-friendly descriptions for mathematical expressions
// in English / Arabic / Hebrew. Converts LaTeX (KaTeX source) into
// spoken text so NVDA / VoiceOver / JAWS / TalkBack do not fall back
// to reading raw "backslash frac brace" tokens.
//
// Why this matters:
//   KaTeX renders to HTML+MathML, but until MathML aria-attributes
//   are honoured everywhere (Safari lags, NVDA on Windows is partial),
//   assistive tech reads the LaTeX source verbatim. For Hebrew/Arabic
//   students that means they hear English LaTeX tokens instead of the
//   math in their own language — which is unusable.
//
// Who calls this:
//   - QuestionCard, QuestionFigure, MasteryMap, WorkedExamplePanel
//   - Anywhere KaTeX is rendered server-side or client-side; the aria
//     label is computed once per expression and emitted on the wrapping
//     <bdi dir="ltr"> element (see useMathRenderer.ts on the frontend).
//
// Design contract:
//   - Deterministic: same (latex, locale) → same string, no stateful
//     caching. Callers can memoize.
//   - Dialect-stable: uses standard Modern Standard Arabic / Hebrew
//     math terminology documented in docs/content/arabic-math-lexicon.md
//     and aligned with Ministry of Education textbooks (Bagrut / IL) and
//     Arab-sector school glossaries (PA curriculum, Arab-sector Israel).
//   - Token-level, not textual replace: we tokenise the LaTeX so
//     "\sin(x) + 2" doesn't get "\sin" butchered into "ss i n" when
//     Arabic/Hebrew replacements run over partial strings.
//
// Scope limits (NOT a LaTeX parser):
//   - Handles the KaTeX subset Cena emits: arithmetic, trig, log, sqrt,
//     frac, integrals (definite/indefinite), sums, limits, Greek letters,
//     subscripts, superscripts, comparison ops, infinity, plus/minus.
//   - Nested \frac and higher-order power towers fall back to a
//     best-effort sequential description — they are extremely rare in
//     Bagrut / Tawjihi material and a depth-sensitive renderer would
//     violate the <500 LOC file rule.
// =============================================================================

using System.Collections.Generic;
using System.Text;

namespace Cena.Infrastructure.Accessibility;

/// <summary>
/// Produces screen-reader-friendly spoken text for LaTeX / KaTeX math
/// expressions in English, Arabic, and Hebrew. Intended to be set as
/// aria-label on the wrapping &lt;bdi dir="ltr"&gt; element that displays
/// the visual KaTeX render.
/// </summary>
public static class MathAriaLabels
{
    /// <summary>Primary subtag for English.</summary>
    public const string LocaleEnglish = "en";
    /// <summary>Primary subtag for Arabic.</summary>
    public const string LocaleArabic = "ar";
    /// <summary>Primary subtag for Hebrew.</summary>
    public const string LocaleHebrew = "he";

    /// <summary>
    /// Convert a LaTeX/KaTeX expression to screen-reader text in the given locale.
    /// </summary>
    /// <param name="latex">Raw LaTeX source, e.g. "\\frac{x+1}{2}".</param>
    /// <param name="locale">BCP-47 primary tag: "en", "ar", or "he". Unknown locales fall back to English.</param>
    /// <returns>Spoken text, whitespace-normalised. Never null; empty string if input is null/empty.</returns>
    public static string ToAriaLabel(string? latex, string? locale)
    {
        if (string.IsNullOrWhiteSpace(latex))
            return string.Empty;

        var tokens = Tokenize(latex!);
        var lexicon = ResolveLexicon(locale);
        var sb = new StringBuilder(latex!.Length * 2);

        Render(tokens, lexicon, sb);

        return Normalize(sb.ToString());
    }

    // ── Tokenisation ────────────────────────────────────────────────────────

    private enum TokenKind { Command, LBrace, RBrace, Caret, Underscore, Text }

    private readonly struct Token
    {
        public Token(TokenKind kind, string value) { Kind = kind; Value = value; }
        public TokenKind Kind { get; }
        public string Value { get; }
    }

    private static List<Token> Tokenize(string latex)
    {
        var tokens = new List<Token>(latex.Length);
        var i = 0;

        while (i < latex.Length)
        {
            var c = latex[i];

            if (c == '\\')
            {
                var j = i + 1;
                // Support single-char commands (\{ \} \, \; \! \|)
                if (j < latex.Length && !char.IsLetter(latex[j]))
                {
                    tokens.Add(new Token(TokenKind.Command, latex.Substring(i, 2)));
                    i = j + 1;
                    continue;
                }
                while (j < latex.Length && char.IsLetter(latex[j]))
                    j++;
                var cmd = latex.Substring(i, j - i);
                tokens.Add(new Token(TokenKind.Command, cmd));
                i = j;
                continue;
            }
            if (c == '{') { tokens.Add(new Token(TokenKind.LBrace, "{")); i++; continue; }
            if (c == '}') { tokens.Add(new Token(TokenKind.RBrace, "}")); i++; continue; }
            if (c == '^') { tokens.Add(new Token(TokenKind.Caret, "^")); i++; continue; }
            if (c == '_') { tokens.Add(new Token(TokenKind.Underscore, "_")); i++; continue; }

            // Collect a run of text up to the next special char
            var k = i;
            while (k < latex.Length && latex[k] != '\\' && latex[k] != '{' && latex[k] != '}'
                   && latex[k] != '^' && latex[k] != '_')
                k++;
            tokens.Add(new Token(TokenKind.Text, latex.Substring(i, k - i)));
            i = k;
        }
        return tokens;
    }

    // ── Rendering ───────────────────────────────────────────────────────────

    private static void Render(List<Token> tokens, Lexicon lx, StringBuilder sb)
    {
        var i = 0;
        while (i < tokens.Count)
        {
            var t = tokens[i];
            switch (t.Kind)
            {
                case TokenKind.Command:
                    i = RenderCommand(tokens, i, lx, sb);
                    break;
                case TokenKind.Caret:
                    i = RenderPower(tokens, i + 1, lx, sb);
                    break;
                case TokenKind.Underscore:
                    i = RenderSub(tokens, i + 1, lx, sb);
                    break;
                case TokenKind.LBrace:
                case TokenKind.RBrace:
                    i++; // braces are structural, never spoken
                    break;
                case TokenKind.Text:
                    sb.Append(SpeakText(t.Value, lx));
                    i++;
                    break;
                default:
                    i++;
                    break;
            }
        }
    }

    private static int RenderCommand(List<Token> tokens, int i, Lexicon lx, StringBuilder sb)
    {
        var cmd = tokens[i].Value;
        switch (cmd)
        {
            case "\\frac":
                {
                    var (num, afterNum) = ReadGroup(tokens, i + 1);
                    var (den, afterDen) = ReadGroup(tokens, afterNum);
                    sb.Append(' ').Append(lx.FractionOpen).Append(' ');
                    var inner = new StringBuilder();
                    Render(num, lx, inner); sb.Append(inner.ToString().Trim());
                    sb.Append(' ').Append(lx.Over).Append(' ');
                    inner.Clear();
                    Render(den, lx, inner); sb.Append(inner.ToString().Trim());
                    sb.Append(' ').Append(lx.FractionClose).Append(' ');
                    return afterDen;
                }
            case "\\sqrt":
                {
                    var (rad, after) = ReadGroup(tokens, i + 1);
                    sb.Append(' ').Append(lx.SqrtOpen).Append(' ');
                    var inner = new StringBuilder();
                    Render(rad, lx, inner); sb.Append(inner.ToString().Trim());
                    sb.Append(' ').Append(lx.SqrtClose).Append(' ');
                    return after;
                }
            case "\\int":
                sb.Append(' ').Append(lx.Integral).Append(' '); return i + 1;
            case "\\sum":
                sb.Append(' ').Append(lx.Sum).Append(' '); return i + 1;
            case "\\prod":
                sb.Append(' ').Append(lx.Product).Append(' '); return i + 1;
            case "\\lim":
                sb.Append(' ').Append(lx.Limit).Append(' '); return i + 1;
            case "\\infty":
                sb.Append(' ').Append(lx.Infinity).Append(' '); return i + 1;
            case "\\pm":
                sb.Append(' ').Append(lx.PlusMinus).Append(' '); return i + 1;
            case "\\mp":
                sb.Append(' ').Append(lx.MinusPlus).Append(' '); return i + 1;
            case "\\leq":
            case "\\le":
                sb.Append(' ').Append(lx.LessEq).Append(' '); return i + 1;
            case "\\geq":
            case "\\ge":
                sb.Append(' ').Append(lx.GreaterEq).Append(' '); return i + 1;
            case "\\neq":
            case "\\ne":
                sb.Append(' ').Append(lx.NotEq).Append(' '); return i + 1;
            case "\\approx":
                sb.Append(' ').Append(lx.Approx).Append(' '); return i + 1;
            case "\\cdot":
            case "\\times":
                sb.Append(' ').Append(lx.Times).Append(' '); return i + 1;
            case "\\div":
                sb.Append(' ').Append(lx.DividedBy).Append(' '); return i + 1;
            // Functions
            case "\\sin": sb.Append(' ').Append(lx.Sin).Append(' '); return i + 1;
            case "\\cos": sb.Append(' ').Append(lx.Cos).Append(' '); return i + 1;
            case "\\tan": sb.Append(' ').Append(lx.Tan).Append(' '); return i + 1;
            case "\\cot": sb.Append(' ').Append(lx.Cot).Append(' '); return i + 1;
            case "\\sec": sb.Append(' ').Append(lx.Sec).Append(' '); return i + 1;
            case "\\csc": sb.Append(' ').Append(lx.Csc).Append(' '); return i + 1;
            case "\\log": sb.Append(' ').Append(lx.Log).Append(' '); return i + 1;
            case "\\ln":  sb.Append(' ').Append(lx.Ln).Append(' '); return i + 1;
            case "\\exp": sb.Append(' ').Append(lx.Exp).Append(' '); return i + 1;
            // Greek
            case "\\alpha": sb.Append(' ').Append(lx.Alpha).Append(' '); return i + 1;
            case "\\beta":  sb.Append(' ').Append(lx.Beta).Append(' '); return i + 1;
            case "\\gamma": sb.Append(' ').Append(lx.Gamma).Append(' '); return i + 1;
            case "\\delta": sb.Append(' ').Append(lx.DeltaLower).Append(' '); return i + 1;
            case "\\Delta": sb.Append(' ').Append(lx.DeltaUpper).Append(' '); return i + 1;
            case "\\theta": sb.Append(' ').Append(lx.Theta).Append(' '); return i + 1;
            case "\\lambda": sb.Append(' ').Append(lx.Lambda).Append(' '); return i + 1;
            case "\\mu":    sb.Append(' ').Append(lx.Mu).Append(' '); return i + 1;
            case "\\pi":    sb.Append(' ').Append(lx.Pi).Append(' '); return i + 1;
            case "\\sigma": sb.Append(' ').Append(lx.Sigma).Append(' '); return i + 1;
            case "\\phi":   sb.Append(' ').Append(lx.Phi).Append(' '); return i + 1;
            case "\\omega": sb.Append(' ').Append(lx.Omega).Append(' '); return i + 1;
            case "\\rightarrow":
            case "\\to":
                sb.Append(' ').Append(lx.Arrow).Append(' '); return i + 1;
            case "\\{": sb.Append(" { "); return i + 1;
            case "\\}": sb.Append(" } "); return i + 1;
            case "\\,": case "\\;": case "\\!": case "\\ ":
                sb.Append(' '); return i + 1;
            default:
                // Unknown command — strip backslash and speak verbatim so
                // authors can see their token wasn't mapped (rather than
                // silently dropping meaning).
                sb.Append(' ').Append(cmd.TrimStart('\\')).Append(' ');
                return i + 1;
        }
    }

    private static int RenderPower(List<Token> tokens, int i, Lexicon lx, StringBuilder sb)
    {
        var (grp, after) = ReadGroup(tokens, i);
        var literal = StringifyGroup(grp).Trim();
        if (literal == "2") { sb.Append(' ').Append(lx.Squared).Append(' '); return after; }
        if (literal == "3") { sb.Append(' ').Append(lx.Cubed).Append(' '); return after; }
        sb.Append(' ').Append(lx.ToThePowerOf).Append(' ');
        var inner = new StringBuilder();
        Render(grp, lx, inner); sb.Append(inner.ToString().Trim()).Append(' ');
        return after;
    }

    private static int RenderSub(List<Token> tokens, int i, Lexicon lx, StringBuilder sb)
    {
        var (grp, after) = ReadGroup(tokens, i);
        sb.Append(' ').Append(lx.Subscript).Append(' ');
        var inner = new StringBuilder();
        Render(grp, lx, inner); sb.Append(inner.ToString().Trim()).Append(' ');
        return after;
    }

    private static (List<Token> group, int after) ReadGroup(List<Token> tokens, int i)
    {
        if (i >= tokens.Count) return (new List<Token>(), i);

        // Single-token group (e.g. `x^2` or `x_i`, not `x^{2+1}`).
        // LaTeX rule: a superscript/subscript without braces applies to
        // exactly one "atom". If the next token is a Text run with more
        // than one character, only the first char is consumed and the
        // remainder is pushed back into the token stream.
        if (tokens[i].Kind != TokenKind.LBrace)
        {
            if (tokens[i].Kind == TokenKind.Text && tokens[i].Value.Length > 1)
            {
                var head = tokens[i].Value[0].ToString();
                var tail = tokens[i].Value.Substring(1);
                // Replace the current token with its tail so the caller
                // resumes at the remaining text. We return the head as a
                // single-char group.
                tokens[i] = new Token(TokenKind.Text, tail);
                return (new List<Token> { new(TokenKind.Text, head) }, i);
            }
            return (new List<Token> { tokens[i] }, i + 1);
        }

        var depth = 1;
        var j = i + 1;
        var result = new List<Token>();
        while (j < tokens.Count && depth > 0)
        {
            var tk = tokens[j];
            if (tk.Kind == TokenKind.LBrace) depth++;
            else if (tk.Kind == TokenKind.RBrace) { depth--; if (depth == 0) break; }
            result.Add(tk);
            j++;
        }
        return (result, j + 1);
    }

    private static string StringifyGroup(List<Token> group)
    {
        var sb = new StringBuilder();
        foreach (var t in group) sb.Append(t.Value);
        return sb.ToString();
    }

    // ── Text-node speaking ─────────────────────────────────────────────────

    private static string SpeakText(string text, Lexicon lx)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            switch (ch)
            {
                case '+': sb.Append(' ').Append(lx.Plus).Append(' '); break;
                case '-': sb.Append(' ').Append(lx.Minus).Append(' '); break;
                case '=': sb.Append(' ').Append(lx.EqualsWord).Append(' '); break;
                case '<': sb.Append(' ').Append(lx.LessThan).Append(' '); break;
                case '>': sb.Append(' ').Append(lx.GreaterThan).Append(' '); break;
                case '(': sb.Append(' ').Append(lx.OpenParen).Append(' '); break;
                case ')': sb.Append(' ').Append(lx.CloseParen).Append(' '); break;
                case '[': sb.Append(' ').Append(lx.OpenBracket).Append(' '); break;
                case ']': sb.Append(' ').Append(lx.CloseBracket).Append(' '); break;
                case ',': sb.Append(' ').Append(lx.Comma).Append(' '); break;
                case '.': sb.Append(lx.Point); break;
                case '·':
                case '×': sb.Append(' ').Append(lx.Times).Append(' '); break;
                case '÷': sb.Append(' ').Append(lx.DividedBy).Append(' '); break;
                case '≤': sb.Append(' ').Append(lx.LessEq).Append(' '); break;
                case '≥': sb.Append(' ').Append(lx.GreaterEq).Append(' '); break;
                case '≠': sb.Append(' ').Append(lx.NotEq).Append(' '); break;
                case '∞': sb.Append(' ').Append(lx.Infinity).Append(' '); break;
                case '∫': sb.Append(' ').Append(lx.Integral).Append(' '); break;
                case '∑': sb.Append(' ').Append(lx.Sum).Append(' '); break;
                case '∏': sb.Append(' ').Append(lx.Product).Append(' '); break;
                case '√': sb.Append(' ').Append(lx.SqrtOpen).Append(' '); break;
                case ' ': sb.Append(' '); break;
                default:
                    sb.Append(ch);
                    break;
            }
        }
        return sb.ToString();
    }

    private static string Normalize(string s)
    {
        var parts = s.Split(new[] { ' ', '\t', '\n', '\r' }, System.StringSplitOptions.RemoveEmptyEntries);
        return string.Join(" ", parts);
    }

    // ── Lexicons ───────────────────────────────────────────────────────────

    private sealed class Lexicon
    {
        public required string Plus { get; init; }
        public required string Minus { get; init; }
        public required string EqualsWord { get; init; }
        public required string LessThan { get; init; }
        public required string GreaterThan { get; init; }
        public required string LessEq { get; init; }
        public required string GreaterEq { get; init; }
        public required string NotEq { get; init; }
        public required string Approx { get; init; }
        public required string PlusMinus { get; init; }
        public required string MinusPlus { get; init; }
        public required string Times { get; init; }
        public required string DividedBy { get; init; }
        public required string OpenParen { get; init; }
        public required string CloseParen { get; init; }
        public required string OpenBracket { get; init; }
        public required string CloseBracket { get; init; }
        public required string Comma { get; init; }
        public required string Point { get; init; }
        public required string Infinity { get; init; }
        public required string Arrow { get; init; }
        public required string Squared { get; init; }
        public required string Cubed { get; init; }
        public required string ToThePowerOf { get; init; }
        public required string Subscript { get; init; }
        public required string FractionOpen { get; init; }
        public required string FractionClose { get; init; }
        public required string Over { get; init; }
        public required string SqrtOpen { get; init; }
        public required string SqrtClose { get; init; }
        public required string Integral { get; init; }
        public required string Sum { get; init; }
        public required string Product { get; init; }
        public required string Limit { get; init; }
        public required string Sin { get; init; }
        public required string Cos { get; init; }
        public required string Tan { get; init; }
        public required string Cot { get; init; }
        public required string Sec { get; init; }
        public required string Csc { get; init; }
        public required string Log { get; init; }
        public required string Ln { get; init; }
        public required string Exp { get; init; }
        public required string Alpha { get; init; }
        public required string Beta { get; init; }
        public required string Gamma { get; init; }
        public required string DeltaLower { get; init; }
        public required string DeltaUpper { get; init; }
        public required string Theta { get; init; }
        public required string Lambda { get; init; }
        public required string Mu { get; init; }
        public required string Pi { get; init; }
        public required string Sigma { get; init; }
        public required string Phi { get; init; }
        public required string Omega { get; init; }
    }

    private static Lexicon ResolveLexicon(string? locale) => (locale ?? string.Empty).ToLowerInvariant() switch
    {
        LocaleArabic => ArabicLexicon,
        LocaleHebrew => HebrewLexicon,
        _ => EnglishLexicon,
    };

    // Modern Standard Arabic math vocabulary. Cross-referenced against
    // docs/content/arabic-math-lexicon.md and the Arab-sector Bagrut
    // companion glossary (PA MoE 2024 curriculum + Arab-sector IL).
    private static readonly Lexicon ArabicLexicon = new()
    {
        Plus = "زائد",
        Minus = "ناقص",
        EqualsWord ="يساوي",
        LessThan = "أصغر من",
        GreaterThan = "أكبر من",
        LessEq = "أصغر من أو يساوي",
        GreaterEq = "أكبر من أو يساوي",
        NotEq = "لا يساوي",
        Approx = "يقارب",
        PlusMinus = "زائد أو ناقص",
        MinusPlus = "ناقص أو زائد",
        Times = "ضرب",
        DividedBy = "قسمة على",
        OpenParen = "فتح قوس",
        CloseParen = "غلق قوس",
        OpenBracket = "فتح قوس مربع",
        CloseBracket = "غلق قوس مربع",
        Comma = "فاصلة",
        Point = "فاصلة عشرية",
        Infinity = "ما لا نهاية",
        Arrow = "يؤول إلى",
        Squared = "تربيع",
        Cubed = "تكعيب",
        ToThePowerOf = "أس",
        Subscript = "ذيل",
        FractionOpen = "الكسر",
        FractionClose = "نهاية الكسر",
        Over = "على",
        SqrtOpen = "الجذر التربيعي",
        SqrtClose = "نهاية الجذر",
        Integral = "تكامل",
        Sum = "مجموع",
        Product = "جداء",
        Limit = "نهاية",
        // Arab-sector convention: sin=جا cos=جتا tan=ظا (short forms used
        // in textbooks); NVDA+eSpeak render these as the full word.
        Sin = "جا",
        Cos = "جتا",
        Tan = "ظا",
        Cot = "ظتا",
        Sec = "قا",
        Csc = "قتا",
        Log = "لو",
        Ln = "لن",
        Exp = "أس",
        Alpha = "ألفا",
        Beta = "بيتا",
        Gamma = "جاما",
        DeltaLower = "دلتا",
        DeltaUpper = "دلتا كبيرة",
        Theta = "ثيتا",
        Lambda = "لامبدا",
        Mu = "مو",
        Pi = "باي",
        Sigma = "سيجما",
        Phi = "فاي",
        Omega = "أوميجا",
    };

    // Modern Hebrew math vocabulary — aligned with Israeli Ministry of
    // Education textbooks (matkonet bagrut) and NVDA-Hebrew conventions.
    private static readonly Lexicon HebrewLexicon = new()
    {
        Plus = "ועוד",
        Minus = "פחות",
        EqualsWord ="שווה",
        LessThan = "קטן מ",
        GreaterThan = "גדול מ",
        LessEq = "קטן או שווה ל",
        GreaterEq = "גדול או שווה ל",
        NotEq = "שונה מ",
        Approx = "בערך",
        PlusMinus = "פלוס מינוס",
        MinusPlus = "מינוס פלוס",
        Times = "כפול",
        DividedBy = "חלקי",
        OpenParen = "פתיחת סוגריים",
        CloseParen = "סגירת סוגריים",
        OpenBracket = "פתיחת סוגריים מרובעים",
        CloseBracket = "סגירת סוגריים מרובעים",
        Comma = "פסיק",
        Point = "נקודה עשרונית",
        Infinity = "אינסוף",
        Arrow = "שואף ל",
        Squared = "בריבוע",
        Cubed = "בשלישית",
        ToThePowerOf = "בחזקת",
        Subscript = "אינדקס",
        FractionOpen = "השבר",
        FractionClose = "סוף השבר",
        Over = "חלקי",
        SqrtOpen = "שורש ריבועי של",
        SqrtClose = "סוף השורש",
        Integral = "אינטגרל",
        Sum = "סכום",
        Product = "מכפלה",
        Limit = "גבול",
        Sin = "סינוס",
        Cos = "קוסינוס",
        Tan = "טנגנס",
        Cot = "קוטנגנס",
        Sec = "סקנס",
        Csc = "קוסקנס",
        Log = "לוג",
        Ln = "לן",
        Exp = "אקספוננט",
        Alpha = "אלפא",
        Beta = "בטא",
        Gamma = "גמא",
        DeltaLower = "דלתא",
        DeltaUpper = "דלתא גדולה",
        Theta = "תטא",
        Lambda = "למדא",
        Mu = "מיו",
        Pi = "פאי",
        Sigma = "סיגמא",
        Phi = "פי",
        Omega = "אומגא",
    };

    private static readonly Lexicon EnglishLexicon = new()
    {
        Plus = "plus",
        Minus = "minus",
        EqualsWord ="equals",
        LessThan = "less than",
        GreaterThan = "greater than",
        LessEq = "less than or equal to",
        GreaterEq = "greater than or equal to",
        NotEq = "not equal to",
        Approx = "approximately",
        PlusMinus = "plus or minus",
        MinusPlus = "minus or plus",
        Times = "times",
        DividedBy = "divided by",
        OpenParen = "open paren",
        CloseParen = "close paren",
        OpenBracket = "open bracket",
        CloseBracket = "close bracket",
        Comma = "comma",
        Point = " point ",
        Infinity = "infinity",
        Arrow = "approaches",
        Squared = "squared",
        Cubed = "cubed",
        ToThePowerOf = "to the power of",
        Subscript = "sub",
        FractionOpen = "the fraction",
        FractionClose = "end fraction",
        Over = "over",
        SqrtOpen = "the square root of",
        SqrtClose = "end root",
        Integral = "integral of",
        Sum = "sum of",
        Product = "product of",
        Limit = "limit",
        Sin = "sine",
        Cos = "cosine",
        Tan = "tangent",
        Cot = "cotangent",
        Sec = "secant",
        Csc = "cosecant",
        Log = "log",
        Ln = "natural log",
        Exp = "exp",
        Alpha = "alpha",
        Beta = "beta",
        Gamma = "gamma",
        DeltaLower = "delta",
        DeltaUpper = "capital delta",
        Theta = "theta",
        Lambda = "lambda",
        Mu = "mu",
        Pi = "pi",
        Sigma = "sigma",
        Phi = "phi",
        Omega = "omega",
    };
}
