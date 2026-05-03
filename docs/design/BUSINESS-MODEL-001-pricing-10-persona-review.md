# Business Model — 10-Persona Pricing Review

**Date**: 2026-04-22
**Status**: Discussion / open for triage
**Owner**: Claude Code coordinator
**Companion**: [PHOTO-UPLOAD-DIAGNOSTIC-001-10-persona-review.md](./PHOTO-UPLOAD-DIAGNOSTIC-001-10-persona-review.md)

---

## What's being reviewed

Three-tier monthly subscription with asymmetric-dominance decoy in the middle, plus a sibling add-on:

| Tier | Price (₪/mo) | Scope |
|------|--------------|-------|
| **Basic** | 79 | Core practice, Haiku-first routing with capped Sonnet fallback for complex problems, 1 student, no photo diagnostic |
| **Plus** *(decoy)* | 229 | Almost-Premium: photo diagnostic included (capped 20/mo), Sonnet for complex, **no parent/teacher dashboard**, 1 student |
| **Premium** *(target)* | 249 | Everything in Plus + unlimited diagnostic (soft-capped), parent/teacher dashboard, tutor-handoff report, priority support, 1 student |
| Sibling add-on | +149 each | Any tier; additional linked student, same household |

**Claimed unit economics at Premium (single seat):** ~₪207 net after 17% VAT and ~3% payment processing → $54 net. COGS $10–25 (LLM tier-mix dependent). Contribution ≈ $30–45/mo.

**Assumed conversion mix for blended ARPU**: 40% Basic / 5% Plus / 55% Premium → blended ARPU ≈ ₪157/mo → $43/mo.

---

## Review methodology

Ten personas, each asked: *what about this pricing breaks for you, and what single change would make it work?* Reviews below are distillations, not verbatim transcripts.

---

### 1. Cost-conscious parent — Beer Sheva, 2 kids (grade 10 + grade 12)

249 + 149 = **398₪/mo × 10 Bagrut-cycle months = 3,980₪/year.** That's more than a semester of private tutoring at neighborhood rates. This parent will not commit to Premium on day 1; they'll start on Basic (79₪) and only upgrade if they see grade impact within 30 days.

**Implication:** Premium conversion in the price-sensitive majority is gated by *observable early wins*. Not marketing copy — a real artifact.

**Ask:** 30-day money-back guarantee *and* an automated "2-week progress report" email that shows concrete improvement (accuracy on specific topics, time-on-task, readiness score delta). The first invoice should be mentally pre-paid by value already delivered.

---

### 2. High-SES Tel Aviv parent

249₪ is invisible; 500₪ would also be invisible. The blocker is *trust* and *time*, not price. Will upgrade to Premium instantly if onboarding takes under 5 minutes and a weekly email arrives.

**Implication:** For this segment, **the pricing page is not the bottleneck — the signal-of-quality is.** They need reassurance it's not "another cheap edtech app."

**Ask:** Named school partnerships listed on the pricing page, real student outcome testimonials (anonymized), and the parent dashboard should look like a Bloomberg terminal, not a kiddie app. Over-polish for this segment; they'll forgive price, not cheap UX.

---

### 3. Skeptical engineer-parent (rare but vocal on WhatsApp groups & Reddit equivalent)

Sees the decoy instantly. If the Plus–Premium delta is "20₪ more for obviously more," they read it as manipulation and post about it.

**Implication:** **Trust erosion cascades through networks.** One viral "the middle tier is a trick" post in an active parent WhatsApp group costs more than the decoy ever gains.

**Ask:** Plus must be a *real, defensible* tier — at least one meaningful feature that some people genuinely prefer over Premium. Proposed: Plus has unlimited photo diagnostic but no dashboard; Premium adds dashboard + tutor-handoff. The dashboard is genuinely an opt-in (some parents find it invasive) — that justifies Plus existing.

---

### 4. 11th-grade student (actual end user)

"My parents pay, I use." Doesn't read tier names. Cares about one thing: does the product block them mid-session?

**Implication:** **Basic's "Haiku-only" framing is dangerous.** If a student hits a Calc-III-style problem on Basic and gets a visibly worse explanation, they'll tell the parent "this doesn't work" and the whole account churns. The parent blames the product, not the tier.

**Ask:** Basic hints must be "simpler but correct," not "worse." Route complex problems to Sonnet even on Basic, but cap at e.g. 20/week. Over the cap, degrade gracefully ("your session limit is reached — come back tomorrow or upgrade"). Never let the student feel they got a broken answer.

---

### 5. Haredi family with 5 kids (grade 8 → grade 12)

Per-seat pricing breaks them completely. 249 + 149×4 = **845₪/mo.** They will share one account (killing your engagement data) or leave.

**Implication:** **A true family plan is a separate market, not a derivative of the single-seat SKU.** Ignoring this segment is defensible; serving them with the default SKU is not.

**Ask:** Cap household pricing — e.g. 249 + 149 + 149 = 547₪ for 3 students, then flat 99₪/additional kid beyond that. Or better: skip this retail market entirely and sell into Haredi school networks at B2B pricing (~35₪/student/month). Don't pretend the retail SKU works for them.

---

### 6. Arab-Israeli family — Nazareth, Arabic-dominant parents

Bagrut matters equally. Price sensitivity ≈ general Israeli middle class. **Critical blocker: Arabic language parity.** If the parent dashboard is Hebrew-only, Premium's 250₪ value prop collapses — the whole point is that *the parent* can monitor progress.

**Implication:** **Premium parent dashboard needs Arabic parity before charging Premium in this segment.** Otherwise you're selling them a feature they can't use and they know it.

**Ask:** Arabic dashboard at Premium launch (not "v1.1"). Also: payment methods beyond credit card (Bit, PayBox, bank transfer) — credit card penetration is lower in some Arabic-Israeli and Haredi segments. A 249₪ plan that can only be paid by Visa is an accidental exclusion filter.

---

### 7. Private tutor — 35, charges 150₪/hour, teaches 10 students/week

Expected reaction: "This competes with me at the bottom but can't replace me at the top." Parents paying 1,200₪/month for 8 tutor-hours will use Cena as a *supplement*. Tutors aren't going away — accountability and human relationship are what the tutor sells.

**Implication:** **Premium should flip tutors from competitors to allies.** Build a feature that serves the tutor, not just displaces them.

**Ask:** Tutor-handoff report at Premium — exportable/shareable PDF saying "this week your student practiced X topics, struggled with Y, mastered Z." The parent forwards it to the tutor before their session. Tutor prepares better, charges their normal rate, recommends Cena to next student. Converts a competitor into a distribution channel.

---

### 8. High school math coordinator (B2B buyer, ~400 12th-graders)

249₪/kid × 400 = 99,600₪/mo = ~1.2M₪/year. **No school district pays retail.** Wants volume pricing, SSO, and a teacher dashboard. If the coordinator hears parents are paying 249₪ retail when their district could have bought seats at 30–40₪, the district deal is dead (pricing leak embarrasses the buyer).

**Implication:** **The B2B SKU must exist from day 1 at a clearly-different price, not as "discounted retail."** Parents and schools should not discover each other's prices.

**Ask:** Dedicated school tier at ~35₪/student/mo with classroom dashboard, teacher-assigned practice, admin SSO. Gate features away from this SKU that parents pay for (parent dashboard stays retail-only), so the tiers are genuinely non-substitutable. Separate sales motion, separate contract, separate T&C.

---

### 9. Product/growth lead

Decoy only works if Premium conversion hits ~55%. If the actual mix lands at 70% Basic / 3% Plus / 27% Premium, blended ARPU is ₪130, not ₪157, and the contribution model breaks.

**Implication:** **First 90 days post-launch are a pricing-discovery phase, not a locked-in SKU.** Expect 2–3 rounds of A/B testing on feature-mix across tiers.

**Ask:**
1. A/B the Plus feature mix (include/exclude dashboard, diagnostic cap at 10 vs 20/mo, etc.) in first 60 days.
2. Offer annual prepay: 2,490₪ for 10 months (one month free). Lock LTV, front-load cash, reduce seasonal churn.
3. Instrument downgrade and churn reasons — the question isn't just conversion, it's *which tier people drop to and why.*

---

### 10. CFO / unit economics

Blended ARPU ₪157 → $43 → net $36 after VAT/processing → contribution $16–26/student/mo after COGS $10–20.

CAC for Israeli edtech B2C: $30–80 per paid conversion. **Payback: 2–6 months. Acceptable, not exceptional.**

Structural risks:
- **Seasonality.** Bagrut prep is concentrated Sep–July. Summer churn is severe. Annual prepay mitigates but doesn't eliminate.
- **COGS volatility.** If Premium users actually use "unlimited" as unlimited, LLM costs blow out fast. A single heavy user can consume $20/mo in Sonnet calls.
- **"Unlimited" is marketing, not policy.** Soft caps are non-negotiable.

**Ask:**
1. Soft cap Premium at 500 diagnostic calls/mo, 2000 hint requests/mo. When hit: friendly upsell UX ("you're in the top 1% of users! book a 1:1 tutor session?"), not a hard block.
2. Annual prepay as default-offered alternative on checkout (increases LTV 20–40% in comparable SaaS).
3. Monthly contribution-margin dashboard per tier, updated weekly in first 90 days. If Premium margin drops below $20/mo, escalate.

---

## Synthesis — what changes in the design

| # | Change | Trigger persona |
|---|--------|-----------------|
| 1 | Basic routes complex problems to Sonnet (capped), not hard-gated Haiku | #4 (student UX) |
| 2 | Plus has a real differentiator (unlimited diagnostic, no dashboard) — not just "more of Basic" | #3 (trust) |
| 3 | Premium parent/teacher dashboard ships with Arabic parity, not post-launch | #6 (Arabic-Israeli market) |
| 4 | Premium includes "tutor handoff" PDF — flips tutor segment from competitor to channel | #7 (tutors) |
| 5 | Sibling pricing caps: 3rd+ sibling at 99₪/mo (household cap) | #5 (large families) |
| 6 | Separate B2B school SKU at ~35₪/student/mo, no feature overlap with Premium parent dashboard | #8 (schools) |
| 7 | Annual prepay at 2,490₪ (10 months for 12) | #9, #10 |
| 8 | Premium "unlimited" is soft-capped (500 diagnostic, 2000 hints) with upsell UX above | #10 (CFO) |
| 9 | 30-day money-back + 2-week auto progress report | #1 (price-sensitive parent) |
| 10 | Alternative payment methods: Bit, PayBox, bank transfer | #6 (Arabic-Israeli, Haredi) |

## Open flags for user triage

- **Decoy legitimacy.** If Plus doesn't convert ≥3% on its own, the decoy is visibly fake and trust-risk real. Monitor and restructure within 90 days if needed.
- **Seasonal churn model not built.** Bagrut cycle means summer churn could be 40%+. Annual prepay mitigates; data needed.
- **Haredi segment decision.** Serve via B2B only, or build a "family plan" SKU? Defer until retail launch data exists.
- **Arabic parity schedule.** Parent dashboard in Arabic is on the critical path for Premium launch in that segment; current i18n state needs audit.

## Unchanged from original proposal

- Price anchors: 79 / 229 / 249 ₪/mo remain plausible (no persona argued them materially wrong)
- Three-tier decoy structure (asymmetric dominance) — valid, but with #2 above making Plus defensible
- Per-seat sibling discount (149₪ base) — confirmed better than household bundle, with caveat #5 for large families
- Premium as target tier, not Plus — confirmed
