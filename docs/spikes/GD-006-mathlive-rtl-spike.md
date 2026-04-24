# GD-006: MathLive RTL Parity Spike

> **Time-box**: 1–2 days
> **Goal**: Determine MathLive's RTL readiness for Arabic and Hebrew math input
> **Status**: Spike created 2026-04-13

## Questions to answer

1. Does MathLive render correctly in `dir="rtl"` containers?
2. Are Arabic math symbols (س for x, جذر for √, Eastern Arabic digits ٠-٩) supported?
3. Does Hebrew math input work with `<bdi dir="ltr">` wrappers per our "Math always LTR" rule?
4. What is the virtual keyboard layout for Arabic and Hebrew?
5. Are there known bugs in MathLive's RTL GitHub issues?

## Test plan

1. Install MathLive in a standalone test page
2. Set `dir="rtl"` on the container
3. Test: type basic algebra (2x + 3 = 7) in RTL mode
4. Test: type Arabic math (٢س + ٣ = ٧) and verify rendering
5. Test: paste LaTeX from KaTeX into MathLive and verify round-trip
6. Measure: input latency on mobile (iPhone Safari, Android Chrome)

## Decision gate

- If MathLive RTL works: proceed with STEP-002 MathInput.vue integration
- If MathLive RTL is broken: evaluate Mathquill or custom input as fallback
- If RTL partially works: file upstream issues, implement workarounds
