// =============================================================================
// Cena Platform — Arabic Math Input Normalizer (ARABIC-001)
//
// Normalizes Arabic math notation to standard ASCII/LaTeX:
//   س → x, ص → y, ع → z (variable names)
//   جذر → √ (square root)
//   ٠-٩ → 0-9 (Eastern Arabic digits)
//   Arabic operators (× → *, ÷ → /)
// =============================================================================

namespace Cena.Infrastructure.Localization;

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
    /// Arabic variable name mapping.
    /// </summary>
    private static readonly Dictionary<string, string> VariableNames = new()
    {
        ["س"] = "x", ["ص"] = "y", ["ع"] = "z",
        ["ن"] = "n", ["م"] = "m", ["ل"] = "l",
        ["ك"] = "k", ["ر"] = "r", ["ت"] = "t",
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
    /// Normalize Arabic math input to standard notation.
    /// </summary>
    public static string Normalize(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;

        var result = input;

        // Replace math terms first (longer strings before single chars)
        foreach (var (arabic, standard) in MathTerms)
        {
            result = result.Replace(arabic, standard);
        }

        // Replace variable names
        foreach (var (arabic, standard) in VariableNames)
        {
            result = result.Replace(arabic, standard);
        }

        // Replace Eastern Arabic digits
        var chars = result.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            if (EasternDigits.TryGetValue(chars[i], out var digit))
                chars[i] = digit;
        }
        result = new string(chars);

        // Replace Arabic operators
        result = result
            .Replace('×', '*')
            .Replace('÷', '/')
            .Replace('−', '-');

        return result;
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

        foreach (var term in VariableNames.Keys)
        {
            if (input.Contains(term)) return true;
        }

        foreach (var term in MathTerms.Keys)
        {
            if (input.Contains(term)) return true;
        }

        return false;
    }
}
