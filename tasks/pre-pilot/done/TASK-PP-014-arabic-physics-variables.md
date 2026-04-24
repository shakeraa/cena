# PP-014: Expand Arabic Math Normalizer with Physics Variables

- **Priority**: Medium — blocks Arabic physics pilot (GD-008)
- **Complexity**: Senior engineer — data mapping expansion
- **Source**: Expert panel review § Localization (Prof. Amjad)

## Problem

`ArabicMathNormalizer` in `src/shared/Cena.Infrastructure/Localization/ArabicMathNormalizer.cs` maps 9 Arabic variable names to Latin equivalents (س→x, ص→y, ع→z, ن→n, م→m, ل→l, ك→k, ر→r, ت→t). These cover pure mathematics but not applied physics.

Arabic physics textbooks used in Israeli Arab schools use additional variable names:
- ق (qof) → F (force, from قوة)
- ت (taa, already mapped to t) → a (acceleration, from تسارع) — CONFLICT with existing mapping
- ج (jeem) → V (volume, from حجم) or G (gravitational constant)
- ط (taa) → E (energy, from طاقة)
- ش (sheen) → W (work, from شغل)
- ز (zayn) → T (period/time, from زمن)
- ح (haa) → v (velocity, from حركة)
- ث (thaa) → θ (angle, from ثيتا — Arabic transliteration of theta)

GD-008 targets an Arabic-first 5-unit physics pilot in Nazareth, Umm al-Fahm, and Rahat. Without these physics variable mappings, Arabic students typing physics equations in their native notation will have their input rejected or misinterpreted.

## Scope

### 1. Add physics variable context

The mapping from ت→t works for math, but in physics ت often means acceleration. This requires context-aware normalization — when the current question is tagged as physics, use the physics mapping.

Add a `NormalizationContext` parameter:

```csharp
public enum NormalizationContext
{
    Mathematics,   // default: ت→t
    Physics        // override: ت→a, ق→F, etc.
}

public static string Normalize(string input, NormalizationContext context = NormalizationContext.Mathematics)
```

### 2. Physics variable mapping

```csharp
private static readonly Dictionary<string, string> PhysicsVariableNames = new()
{
    ["ق"] = "F",   // force (قوة)
    ["ت"] = "a",   // acceleration (تسارع) — overrides math mapping
    ["ج"] = "V",   // volume (حجم)
    ["ط"] = "E",   // energy (طاقة)
    ["ش"] = "W",   // work (شغل)
    ["ز"] = "T",   // period (زمن)
    ["ح"] = "v",   // velocity (حركة)
    ["ث"] = "θ",   // angle (ثيتا)
};
```

### 3. Physics math terms

```csharp
// Additional physics terms
["نيوتن"] = "N",   // Newton (unit)
["جول"] = "J",     // Joule (unit)
["واط"] = "W",     // Watt (unit)
["أمبير"] = "A",   // Ampere (unit)
["فولت"] = "V",    // Volt (unit)
```

## Files to Modify

- `src/shared/Cena.Infrastructure/Localization/ArabicMathNormalizer.cs` — add physics context and mappings
- `src/shared/Cena.Infrastructure.Tests/Localization/ArabicMathNormalizerTests.cs` — NEW: test physics normalization

## Acceptance Criteria

- [ ] Physics variables (ق→F, ط→E, ش→W, etc.) are normalized when context is Physics
- [ ] Math variables (ت→t) are preserved when context is Mathematics
- [ ] Context-dependent override (ت→a in physics, ت→t in math) works correctly
- [ ] Physics units (نيوتن→N, جول→J) are normalized
- [ ] Test: Arabic physics equation "ق = م × ت" normalizes to "F = m × a" in physics context
- [ ] Test: same input normalizes to "F = m × t" in math context (ت→t)
