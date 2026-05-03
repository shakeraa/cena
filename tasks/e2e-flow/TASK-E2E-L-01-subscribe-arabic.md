# TASK-E2E-L-01: Full subscription flow in Arabic

**Status**: Proposed
**Priority**: P1
**Epic**: [EPIC-E2E-L](EPIC-E2E-L-accessibility-i18n.md)
**Tag**: `@i18n @rtl @p1`
**Spec path**: `src/student/full-version/tests/e2e-flow/workflows/subscribe-arabic.spec.ts`
**Prereqs**: none beyond shared fixtures (`tenant`, `authUser`, `stripeScope` — wired in `fixtures/tenant.ts`)

## Journey

Locale = `ar` → run [B-01](TASK-E2E-001-subscription-happy-path.md) → every screen RTL, labels localized, confirm page localized.

## Boundary assertions

| Boundary | Assertion |
| --- | --- |
| `document.dir` | `rtl` throughout |
| i18n | No raw keys visible (`pricing.tier.plus.title`) |
| Math (if any) | Inside `<bdi dir="ltr">` |

## Regression this catches

`dir` attribute not set; raw i18n key leak; RTL bleed into math.

## Done when

- [ ] Spec lands
