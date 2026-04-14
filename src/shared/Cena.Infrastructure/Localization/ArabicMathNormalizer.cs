// =============================================================================
// Cena Platform — Arabic Math Input Normalizer (ARABIC-001)
//
// Normalizes Arabic math notation to standard ASCII/LaTeX:
//   س → x, ص → y, ع → z (variable names)
//   جذر → √ (square root)
//   ٠-٩ → 0-9 (Eastern Arabic digits)
//   Arabic operators (× → *, ÷ → /)
//
// PP-014: Context-aware normalization — Physics context overrides
//         specific variable mappings (e.g. ت→a instead of ت→t).
// PP-015: Single-pass normalization to prevent bidi visual corruption
//         during incremental (keystroke-by-keystroke) processing.
// =============================================================================

using System.Text;

namespace Cena.Infrastructure.Localization;

/// <summary>
/// PP-014: Normalization context determines which variable mappings apply.
/// </summary>
public enum NormalizationContext
{
    Mathematics,
    Physics
}

public static class ArabicMathNormalizer
{
    /// <summary>
    /// Eastern Arabic digit mapping (٠-٩ → 0-9).
    /// </summary>
    private static readonly Dictionary<char, char> EasternDigits = new()
    {
        ['٠'] = '0', ['١'] = '1', ['٢'] = '2', ['٣'] = '3', ['٤'] = '4',
        ['٥'] = '5', ['٦'] = '6', ['٧'] = '7', ['٨'] = '8', ['٩'] = '9',
    };

    /// <summary>
    /// Arabic variable name mapping (mathematics context).
    /// </summary>
    private static readonly Dictionary<string, string> MathVariableNames = new()
    {
        ["س"] = "x", ["ص"] = "y", ["ع"] = "z",
        ["ن"] = "n", ["م"] = "m", ["ل"] = "l",
        ["ك"] = "k", ["ر"] = "r", ["ت"] = "t",
    };

    /// <summary>
    /// PP-014: Arabic variable name mapping (physics context).
    /// Overrides: ت→a (acceleration, not t).
    /// Additions: force, energy, work, period, velocity, angle.
    /// </summary>
    private static readonly Dictionary<string, string> PhysicsVariableNames = new()
    {
        ["س"] = "x", ["ص"] = "y", ["ع"] = "z",
        ["ن"] = "n", ["م"] = "m", ["ل"] = "l",
        ["ك"] = "k", ["ر"] = "r",
        ["ت"] = "a",   // acceleration (تسارع) — overrides math ت→t
        ["ق"] = "F",   // force (قوة)
        ["ج"] = "V",   // volume (حجم)
        ["ط"] = "E",   // energy (طاقة)
        ["ش"] = "W",   // work (شغل)
        ["ز"] = "T",   // period (زمن)
        ["ح"] = "v",   // velocity (حركة)
        ["ث"] = "θ",   // angle (ثيتا)
    };

    /// <summary>
    /// Arabic math term mapping.
    /// </summary>
    private static readonly Dictionary<string, string> MathTerms = new()
    {
        ["جذر"] = "sqrt",
        ["جيب"] = "sin",
        ["جتا"] = "cos",
        ["ظل"] = "tan",
        ["لو"] = "log",
        ["لن"] = "ln",
        ["باي"] = "pi",
        ["نهاية"] = "lim",
        ["تكامل"] = "int",
        ["مشتقة"] = "d/dx",
    };

    /// <summary>
    /// PP-014: Arabic physics unit mapping.
    /// </summary>
    private static readonly Dictionary<string, string> PhysicsUnits = new()
    {
        ["نيوتن"] = "N",
        ["جول"] = "J",
        ["واط"] = "W",
        ["أمبير"] = "A",
        ["فولت"] = "V",
    };

    /// <summary>
    /// PP-014: Normalize Arabic math input with context-aware variable mapping.
    /// PP-015: Uses single-pass replacement to prevent bidi visual corruption.
    /// </summary>
    public static string Normalize(string input, NormalizationContext context = NormalizationContext.Mathematics)
    {
        if (string.IsNullOrEmpty(input)) return input;

        var variableNames = context == NormalizationContext.Physics
            ? PhysicsVariableNames
            : MathVariableNames;

        // PP-015: Single-pass normalization via StringBuilder.
        // Process character by character to avoid intermediate mixed-direction states
        // that cause bidi algorithm reordering artifacts.
        var sb = new StringBuilder(input.Length);
        int i = 0;

        while (i < input.Length)
        {
            // Try multi-char terms first (longest match)
            bool matched = false;

            // Physics units (longest first)
            if (context == NormalizationContext.Physics)
            {
                matched = TryMatchAndReplace(input, i, PhysicsUnits, sb, out var advance);
                if (matched) { i += advance; continue; }
            }

            // Math terms
            matched = TryMatchAndReplace(input, i, MathTerms, sb, out var termAdvance);
            if (matched) { i += termAdvance; continue; }

            // Single-char variable names
            var ch = input[i].ToString();
            if (variableNames.TryGetValue(ch, out var varReplacement))
            {
                sb.Append(varReplacement);
                i++;
                continue;
            }

            // Eastern Arabic digits
            if (EasternDigits.TryGetValue(input[i], out var digit))
            {
                sb.Append(digit);
                i++;
                continue;
            }

            // Arabic operators
            var c = input[i];
            sb.Append(c switch
            {
                '×' => '*',
                '÷' => '/',
                '−' => '-',
                _ => c
            });
            i++;
        }

        return sb.ToString();
    }

    /// <summary>
    /// Check if input contains Arabic math characters that need normalization.
    /// </summary>
    public static bool NeedsNormalization(string input)
    {
        if (string.IsNullOrEmpty(input)) return false;

        foreach (var c in input)
        {
            if (EasternDigits.ContainsKey(c)) return true;
        }

        foreach (var term in MathVariableNames.Keys)
        {
            if (input.Contains(term)) return true;
        }

        foreach (var term in PhysicsVariableNames.Keys)
        {
            if (input.Contains(term)) return true;
        }

        foreach (var term in MathTerms.Keys)
        {
            if (input.Contains(term)) return true;
        }

        return false;
    }

    /// <summary>
    /// Try to match a multi-char term at position i and append replacement.
    /// Returns true if matched, with advance = number of chars consumed.
    /// </summary>
    private static bool TryMatchAndReplace(
        string input, int pos, Dictionary<string, string> terms,
        StringBuilder sb, out int advance)
    {
        // Try longer terms first for greedy matching
        foreach (var (arabic, replacement) in terms.OrderByDescending(t => t.Key.Length))
        {
            if (pos + arabic.Length <= input.Length &&
                input.AsSpan(pos, arabic.Length).SequenceEqual(arabic))
            {
                sb.Append(replacement);
                advance = arabic.Length;
                return true;
            }
        }

        advance = 0;
        return false;
    }
}
