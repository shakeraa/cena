// ═══════════════════════════════════════════════════════════════════════
// Cena Platform — Cultural Context Service (FOC-012.1)
//
// Detects cultural context from language preference to adjust resilience
// scoring. Research (PMC 2022, 2024): Israeli-Jewish students report
// higher individual self-efficacy; Palestinian Arab students build
// resilience through collective support.
//
// NO explicit ethnicity collection — uses language preference as proxy.
// ═══════════════════════════════════════════════════════════════════════

namespace Cena.Actors.Services;

/// <summary>
/// Detects cultural context from student language signals.
/// </summary>
public interface ICulturalContextService
{
    CulturalContext Detect(CulturalContextInput input);
}

public sealed class CulturalContextService : ICulturalContextService
{
    public CulturalContext Detect(CulturalContextInput input)
    {
        // ── Primary signal: onboarding language choice ──
        bool hasHebrew = !string.IsNullOrEmpty(input.OnboardingLanguage)
            && input.OnboardingLanguage.Equals("he", StringComparison.OrdinalIgnoreCase);
        bool hasArabic = !string.IsNullOrEmpty(input.OnboardingLanguage)
            && input.OnboardingLanguage.Equals("ar", StringComparison.OrdinalIgnoreCase);

        // ── Secondary signal: interface language setting ──
        bool interfaceHebrew = !string.IsNullOrEmpty(input.InterfaceLanguage)
            && input.InterfaceLanguage.Equals("he", StringComparison.OrdinalIgnoreCase);
        bool interfaceArabic = !string.IsNullOrEmpty(input.InterfaceLanguage)
            && input.InterfaceLanguage.Equals("ar", StringComparison.OrdinalIgnoreCase);

        // ── Tertiary signal: typing language (most frequently used input language) ──
        bool typingHebrew = !string.IsNullOrEmpty(input.PrimaryTypingLanguage)
            && input.PrimaryTypingLanguage.Equals("he", StringComparison.OrdinalIgnoreCase);
        bool typingArabic = !string.IsNullOrEmpty(input.PrimaryTypingLanguage)
            && input.PrimaryTypingLanguage.Equals("ar", StringComparison.OrdinalIgnoreCase);

        int hebrewSignals = (hasHebrew ? 1 : 0) + (interfaceHebrew ? 1 : 0) + (typingHebrew ? 1 : 0);
        int arabicSignals = (hasArabic ? 1 : 0) + (interfaceArabic ? 1 : 0) + (typingArabic ? 1 : 0);

        // ── Classification ──
        if (hebrewSignals > 0 && arabicSignals > 0)
            return CulturalContext.Bilingual;

        if (arabicSignals >= 1)
            return CulturalContext.ArabicDominant;

        if (hebrewSignals >= 1)
            return CulturalContext.HebrewDominant;

        return CulturalContext.Unknown;
    }
}

// ═══════════════════════════════════════════════════════════════
// TYPES
// ═══════════════════════════════════════════════════════════════

public enum CulturalContext
{
    HebrewDominant,  // Individualist resilience pattern — existing weights
    ArabicDominant,  // Collectivist resilience pattern — higher Recovery weight
    Bilingual,       // Both languages detected — use baseline weights
    Unknown          // Insufficient data — use baseline weights
}

public record CulturalContextInput(
    string? OnboardingLanguage,     // "he" or "ar" — chosen during first setup
    string? InterfaceLanguage,      // Current UI language setting
    string? PrimaryTypingLanguage   // Most frequently used keyboard language
);
