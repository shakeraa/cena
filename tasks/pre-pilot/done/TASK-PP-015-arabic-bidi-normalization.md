# PP-015: Bidi-Aware Arabic Math Normalization

- **Priority**: Medium — prevents visual corruption in mixed Arabic/Latin math
- **Complexity**: Senior engineer — Unicode bidi algorithm understanding required
- **Source**: Expert panel review § Localization (Tamar)

## Problem

`ArabicMathNormalizer.Normalize` in `src/shared/Cena.Infrastructure/Localization/ArabicMathNormalizer.cs:54-88` uses `string.Replace` to substitute Arabic characters with Latin equivalents. This processes left-to-right and does not account for Unicode bidi context.

When a student types mixed Arabic/Latin text like `س² + ص²`:
1. The Arabic س (seen) is RTL, the ² superscript is neutral, and + is neutral
2. After normalization to `x² + y²`, the string is fully LTR
3. But during the replacement, if the string is partially normalized (e.g., `x² + ص²`), the bidi algorithm may reorder the neutral characters (², +) to follow the remaining RTL character (ص), producing a visual like `x² ²ص +` in some rendering contexts

This is especially problematic in live-input scenarios where the normalizer runs on each keystroke.

## Scope

### 1. Pre-normalization: wrap Arabic tokens in bidi isolates

Before substitution, wrap each Arabic token in Unicode First Strong Isolate (U+2068) and Pop Directional Isolate (U+2069) to prevent bidi algorithm interference during incremental replacement:

```csharp
// Wrap each Arabic token to isolate it from surrounding Latin text
foreach (var (arabic, standard) in VariableNames)
{
    result = result.Replace(arabic, $"\u2068{standard}\u2069");
}
// Then strip the isolates after all replacements are done
result = result.Replace("\u2068", "").Replace("\u2069", "");
```

### 2. Alternative: batch replacement

Instead of iterative `string.Replace`, build the output string in a single pass using a `StringBuilder` that processes each character and maps it in place. This avoids intermediate mixed-direction states entirely.

### 3. Test with live-input simulation

Test that normalizing on each keystroke of "س² + ص² = ع²" produces correct output at each intermediate state.

## Files to Modify

- `src/shared/Cena.Infrastructure/Localization/ArabicMathNormalizer.cs` — single-pass normalization or bidi-isolated replacement
- `src/shared/Cena.Infrastructure.Tests/Localization/ArabicMathNormalizerTests.cs` — incremental keystroke tests

## Acceptance Criteria

- [ ] Normalization of `س² + ص²` produces `x² + y²` with correct character ordering
- [ ] Intermediate normalization states (during keystroke-by-keystroke processing) do not produce visually corrupted output
- [ ] Test: normalize "س² + ص² = ع²" character-by-character and verify each intermediate result
- [ ] No Unicode bidi control characters remain in the final output
