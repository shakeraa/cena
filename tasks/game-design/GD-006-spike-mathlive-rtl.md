# GD-006: Technical spike — MathLive RTL parity for Arabic and Hebrew

## Goal
Prove or disprove that MathLive can deliver a usable equation-input experience for Arabic and Hebrew students at Bagrut / AP level, and document the gaps that Cena would have to patch.

## Source
`docs/research/cena-sexy-game-research-2026-04-11.md` — Proposal Point 8 (MathLive keyboard + haptics). Research found zero meta-analytic evidence on RTL math input.

## Work to do
1. Time-boxed spike: 1–2 focused days max
2. Install MathLive in a sandbox page (no production wiring yet)
3. Test cases:
   - EN student enters `\frac{-b \pm \sqrt{b^2-4ac}}{2a}` — baseline
   - AR student types the same in a UI whose containing page is `dir="rtl"`
   - HE student types the same with Hebrew locale
   - Mixed content: Arabic word then an equation then more Arabic (inline)
   - Backspace / navigation keys behave correctly in RTL context
   - Copy-paste from a Bagrut PDF into the input — does rendering survive?
4. Document:
   - Which inputs break
   - Whether the caret moves in the expected direction
   - Whether numeric literals are LTR-isolated correctly inside the RTL container
   - Screen-reader behavior for all three locales
5. Propose a fix plan (Cena patches, MathLive PRs upstream, or fallback input strategy)

## Deliverable
`docs/research/spikes/mathlive-rtl-spike-2026-04-XX.md` — findings + recommendation

## DoD
- Spike doc merged
- Go/no-go decision on using MathLive for AR + HE
- Follow-up tasks filed if MathLive is adopted with patches

## Reporting
Complete with branch + go/no-go recommendation + top 3 blockers.
