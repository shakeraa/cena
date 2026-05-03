# RDY-077 — F12: Parent-controlled time budget (soft cap, no manipulation)

- **Wave**: D (wireframes + dark-pattern scan required pre-build)
- **Priority**: LOW-MED
- **Effort**: 2 engineer-weeks + 1 week UX review
- **Dependencies**: wireframes reviewed for dark-pattern risk (Rami flagged)
- **Source**: [panel review](../../docs/research/cena-panel-review-user-personas-2026-04-17.md) Round 2.F12

## Problem

Rachel (wants time efficiency), Mahmoud (wants honest visibility), Dalia (wants to protect Yael from pressure) each want some parental time control. But Dr. Nadia warned: external motivation can crowd out intrinsic motivation (Deci & Ryan SDT). The feature is at genuine risk of slipping into dark-pattern territory.

## Scope

**Parent controls** (parent console):
- Optional weekly time budget (e.g., 5 hrs/week)
- Time-of-day restrictions (no study after 21:00)
- Topic allow-list for minor-age students

**Student-facing UI** (the critical surface):
- Soft cap with calm gauge — "you and your parent agreed to 5 hours this week, you're at 4.5"
- No FOMO framing ("ONLY 30 MIN LEFT!" = banned)
- No scarcity framing
- No lockout at cap — student chooses to continue or stop (log continuation for parent)
- No countdown timer that turns red

**Pre-build gate** (Rami's demand):
- Wireframes reviewed for dark-pattern risk BEFORE implementation
- Ship-gate scanner extended to cover this surface's copy

## Files to Create / Modify

Do NOT start code until:
- [ ] Wireframes reviewed and approved by Dr. Lior + Dr. Nadia
- [ ] Ship-gate banned-term list extended with FOMO/scarcity variants

Then:
- `src/shared/Cena.Domain/ParentalControls/TimeBudget.cs`
- `src/api/Cena.Admin.Api/Features/ParentConsole/TimeBudgetEndpoint.cs`
- `src/student/full-version/src/components/controls/TimeBudgetGauge.vue`
- `docs/design/parent-time-budget-design.md` — wireframe doc

## Acceptance Criteria

- [ ] Wireframes approved pre-build
- [ ] Ship-gate scanner extended for this surface
- [ ] Time budget is soft cap, not lockout; continuation logged for parent
- [ ] Zero FOMO/scarcity copy in surface (CI enforces)
- [ ] Parent can change budget without student knowing historical budget values (prevents pressure)
- [ ] Session-scoped time-usage data; 30-day retention cap on parent dashboard

## Success Metrics

- **Retention impact (budget-enabled vs not)**: target NOT worse than unbudgeted cohort
- **Student-reported pressure survey**: target <10% report feeling pressured by budget
- **Ship-gate violations**: target 0 in production

## ADR Alignment

- ICO Children's Code Std 11 (parental controls)
- GD-004: anti-dark-pattern; ship-gate enforces
- Deci & Ryan SDT: soft cap preserves autonomy

## Out of Scope

- Hard lockout at cap (by design — soft cap only)
- Budget borrowing / rolling forward (complicates UX, adds pressure)
- Gamified parent-student time negotiation

## Assignee

Unassigned; Dr. Lior for wireframe review; Dr. Nadia for SDT-framing review.
