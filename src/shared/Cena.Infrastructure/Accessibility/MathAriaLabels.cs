// =============================================================================
// Cena Platform — SRE Aria-Labels for Math (A11Y-SRE-001)
//
// Screen-reader-friendly descriptions for mathematical expressions
// in Arabic and Hebrew. Converts LaTeX/KaTeX to spoken text.
// =============================================================================

namespace Cena.Infrastructure.Accessibility;

/// <summary>
/// Generates aria-label text for mathematical expressions in Arabic and Hebrew.
/// Used by QuestionCard, MasteryMap, and QuestionFigure for a11y.
/// </summary>
public static class MathAriaLabels
{
    /// <summary>
    /// Convert a LaTeX expression to screen-reader text in the given locale.
    /// </summary>
    public static string ToAriaLabel(string latex, string locale)
    {
        // Strip LaTeX commands and convert to spoken math
        var spoken = latex
            .Replace("\\frac{", "")
            .Replace("}{", " over ")
            .Replace("}", "")
            .Replace("{", "")
            .Replace("\\sqrt", " square root of ")
            .Replace("\\pi", " pi ")
            .Replace("\\theta", " theta ")
            .Replace("\\alpha", " alpha ")
            .Replace("\\beta", " beta ")
            .Replace("^2", " squared")
            .Replace("^3", " cubed")
            .Replace("^{", " to the power of ")
            .Replace("_", " sub ")
            .Replace("\\cdot", " times ")
            .Replace("\\times", " times ")
            .Replace("\\div", " divided by ")
            .Replace("\\pm", " plus or minus ")
            .Replace("\\leq", " less than or equal to ")
            .Replace("\\geq", " greater than or equal to ")
            .Replace("\\neq", " not equal to ")
            .Replace("\\approx", " approximately ")
            .Replace("\\infty", " infinity ")
            .Replace("\\sin", " sine ")
            .Replace("\\cos", " cosine ")
            .Replace("\\tan", " tangent ")
            .Replace("\\log", " log ")
            .Replace("\\ln", " natural log ");

        return locale switch
        {
            "ar" => ToArabicSpoken(spoken),
            "he" => ToHebrewSpoken(spoken),
            _ => spoken.Trim()
        };
    }

    private static string ToArabicSpoken(string english)
    {
        return english
            .Replace(" squared", " تربيع")
            .Replace(" cubed", " تكعيب")
            .Replace(" square root of ", " جذر تربيعي ")
            .Replace(" over ", " على ")
            .Replace(" times ", " ضرب ")
            .Replace(" divided by ", " قسمة ")
            .Replace(" plus or minus ", " زائد أو ناقص ")
            .Replace(" less than or equal to ", " أصغر من أو يساوي ")
            .Replace(" greater than or equal to ", " أكبر من أو يساوي ")
            .Replace(" not equal to ", " لا يساوي ")
            .Replace(" approximately ", " تقريباً ")
            .Replace(" infinity ", " ما لا نهاية ")
            .Replace(" sine ", " جيب ")
            .Replace(" cosine ", " جيب تمام ")
            .Replace(" tangent ", " ظل ")
            .Trim();
    }

    private static string ToHebrewSpoken(string english)
    {
        return english
            .Replace(" squared", " בריבוע")
            .Replace(" cubed", " בשלישית")
            .Replace(" square root of ", " שורש ריבועי של ")
            .Replace(" over ", " חלקי ")
            .Replace(" times ", " כפול ")
            .Replace(" divided by ", " חלקי ")
            .Replace(" plus or minus ", " פלוס או מינוס ")
            .Replace(" less than or equal to ", " קטן או שווה ל ")
            .Replace(" greater than or equal to ", " גדול או שווה ל ")
            .Replace(" not equal to ", " לא שווה ל ")
            .Replace(" approximately ", " בקירוב ")
            .Replace(" infinity ", " אינסוף ")
            .Replace(" sine ", " סינוס ")
            .Replace(" cosine ", " קוסינוס ")
            .Replace(" tangent ", " טנגנס ")
            .Trim();
    }
}
