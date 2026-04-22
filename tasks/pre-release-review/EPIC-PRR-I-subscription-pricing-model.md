# EPIC-PRR-I: Subscription & Pricing Model

**Priority**: P0 — launch-blocker for B2C revenue; commercial model must be live on day 1
**Effort**: L (6-10 weeks engineering + parallel legal/finance/payments procurement; 2-3 weeks ongoing A/B tuning post-launch)
**Lens consensus**: 10-persona review 2026-04-22 — 10 changes forced (see §2)
**Source docs**:
- [BUSINESS-MODEL-001-pricing-10-persona-review.md](../../docs/design/BUSINESS-MODEL-001-pricing-10-persona-review.md)
- [ADR-0026](../../docs/adr/0026-llm-three-tier-routing.md) (3-tier LLM routing informs per-tier COGS)
- [ADR-0048](../../docs/adr/0048-exam-prep-time-framing.md) (shipgate bans apply to upsell UX)
**Assignee hint**: Shaker (coordinator + pricing owner) + backend (billing) + frontend (pricing page, upgrade UX) + legal (VAT/consumer law) + finance (unit-economics model) + payments-procurement (Stripe + Bit + PayBox)
**Tags**: source=business-model-pricing; type=epic; epic=epic-prr-i; launch-blocker; commercial
**Status**: Not Started — awaiting decision-holder greenlight on 7 open items (§5 below)
**Source**: 10-persona pricing review 2026-04-22
**Tier**: launch (core 3 tiers + sibling) / launch+1 (B2B school SKU + annual prepay + Haredi family plan)
**Related epics**:
- [EPIC-PRR-B](EPIC-PRR-B-llm-routing-governance.md) — 3-tier LLM routing defines per-tier COGS; tiered caps must route through routing governance
- [EPIC-PRR-H](EPIC-PRR-H-student-input-modalities.md) — writing-pad + photo-diagnostic caps map to pricing tiers (basic ~40 HWR calls/mo, premium ~600)
- [EPIC-PRR-J](EPIC-PRR-J-photo-upload-cas-diagnostic-chain.md) — diagnostic upload caps map to this epic's per-tier limits
- [EPIC-PRR-C](EPIC-PRR-C-parent-aggregate-consent.md) — parent dashboard + consent required for Premium tier; Premium cannot ship before PRR-C core done

---

## 1. Epic goal

Ship Cena's retail subscription product with a three-tier decoy-structure pricing model that is:

1. **Commercially defensible** at ₪249/mo Premium anchor (high-SES parents accept, price-sensitive parents upgrade based on observed value).
2. **Architecturally clean** — per-seat sibling discount, not household bundles; unit-economics transparent per tier.
3. **Segment-aware** — separate SKUs for retail parent-pay, B2B schools, and (deferred) large-household families. No pricing leaks between segments.
4. **Protected against abuse** — soft-capped "unlimited" with graceful upsell, not hard blocks.
5. **Payment-inclusive for Israel** — credit card + Bit + PayBox + bank transfer + annual prepay.
6. **Trust-preserving** — decoy tier must be legitimate (at least one differentiator), not a strawman.

## 2. Locked decisions (10-persona review 2026-04-22)

| # | Decision | Trigger persona |
|---|----------|-----------------|
| 1 | Basic tier routes complex problems to Sonnet (capped ~20/week), not hard-gated to Haiku | #4 student UX |
| 2 | Plus tier (decoy) has one real differentiator vs. Basic — **unlimited photo diagnostic + Sonnet-for-complex**; does NOT include parent/teacher dashboard or tutor-handoff | #3 trust / engineer-parent |
| 3 | Premium parent/teacher dashboard ships with **Arabic parity on day 1**, not post-launch. Also Hebrew + English. | #6 Arabic-Israeli market |
| 4 | Premium includes **tutor-handoff PDF** (shareable progress report for external tutors) — flips tutor segment from competitor to channel | #7 private tutor |
| 5 | Sibling pricing cap: first sibling +₪149/mo, second sibling +₪149/mo, **third+ sibling ₪99/mo each** (household cap protects against abandonment by large families) | #5 Haredi large-family |
| 6 | Separate **B2B school SKU** at ~₪35/student/mo with classroom admin dashboard + SSO + teacher-assigned practice. Feature set deliberately non-overlapping with retail Premium (e.g., no parent dashboard in school SKU). Launch+1 acceptable. | #8 school coordinator |
| 7 | **Annual prepay**: ₪2,490/year (10 months for 12) at checkout. Locks LTV, front-loads cash, reduces summer churn. | #9 growth |
| 8 | **Premium "unlimited" = soft-capped**: 500 diagnostic uploads/mo, 2000 hint requests/mo, 100 full-Sonnet escalations/mo. Above cap = graceful upsell UX, not hard block. | #10 CFO |
| 9 | **30-day money-back guarantee** + **2-week auto progress report** (email) for all new subscribers — pre-pays value for price-sensitive cohort | #1 cost-conscious parent |
| 10 | Alternative payment methods on day 1: **credit card (Stripe) + Bit + PayBox + bank transfer**. Credit-card-only is an accidental exclusion filter. | #6 Arabic-Israeli + Haredi + anyone without CC |

## 3. Sub-task ladder (to be created after decision-holder greenlight)

### 3.1 Pricing page + tier visibility (retail)

| ID | Title | Priority | Blocker? |
|---|---|---|---|
| PRR-290 | 3-tier pricing card (Basic ₪79 / Plus ₪229 / Premium ₪249) — HE/AR/EN variants + RTL | P0 | yes |
| PRR-291 | Tier-feature matrix (what's in / out) as data, not hardcoded strings; drives page + checkout + upgrade prompts | P0 | yes |
| PRR-292 | Annual prepay toggle on checkout (₪2,490/yr) with savings badge | P0 | yes |
| PRR-293 | Sibling-add UX on post-purchase account screen (₪149 first/second, ₪99 third+) | P0 | yes |
| PRR-294 | 30-day money-back guarantee display + backend refund workflow | P0 | yes |
| PRR-295 | 2-week auto progress report (email, HE/AR/EN) | P0 | yes |

### 3.2 Billing + payments infrastructure

| ID | Title | Priority | Blocker? |
|---|---|---|---|
| PRR-300 | Subscription billing engine (plan lifecycle: trial / active / past-due / cancelled / refunded) | P0 | yes |
| PRR-301 | Stripe integration (credit card, recurring) with Israel VAT handling | P0 | yes |
| PRR-302 | Bit integration (Israel P2P-dominant payment method) | P0 | **vendor-procurement gate** |
| PRR-303 | PayBox integration | P0 | **vendor-procurement gate** |
| PRR-304 | Bank transfer flow (manual reconciliation in v1; can delay to launch+1 if critical path tight) | P1 | — |
| PRR-305 | VAT-inclusive Israel pricing + invoice generation (Hebrew tax invoice format) | P0 | **legal gate** |
| PRR-306 | Refund workflow (30-day money-back automation) | P0 | yes |

### 3.3 Tier enforcement + usage caps (integration with EPIC-PRR-B and EPIC-PRR-J)

| ID | Title | Priority | Blocker? |
|---|---|---|---|
| PRR-310 | `SubscriptionTier` domain concept + propagation through student-api / actor-host / LLM router | P0 | yes |
| PRR-311 | Per-tier LLM routing policy: Basic=Haiku-first + 20 Sonnet/week; Plus=Sonnet-for-complex unlimited; Premium=Sonnet-unlimited soft-capped | P0 | yes — couples to EPIC-PRR-B |
| PRR-312 | Per-tier photo-diagnostic caps: Basic=0, Plus=20/mo, Premium=100/mo soft + 300/mo hard | P0 | yes — couples to EPIC-PRR-J |
| PRR-313 | Graceful upsell UX when soft cap hit ("you're in the top 1%! book a tutor session?") — not hard-block | P0 | yes |
| PRR-314 | Abuse detection: users >200 uploads/mo flagged for account-sharing review | P1 | — |

### 3.4 Parent dashboard (Premium-tier value driver)

| ID | Title | Priority | Blocker? |
|---|---|---|---|
| PRR-320 | Parent dashboard MVP: progress per linked-student, topic mastery map, diagnostic summary (HE) | P0 | yes |
| PRR-321 | Parent dashboard Arabic parity (full RTL, Arabic math + content strings) | P0 | yes — launch-blocker per persona #6 |
| PRR-322 | Parent dashboard English parity | P1 | — |
| PRR-323 | Weekly email digest for parents (HE/AR/EN) | P0 | yes |
| PRR-324 | Multi-student household view (show all linked siblings in one parent account) | P0 | yes |
| PRR-325 | Tutor-handoff PDF export ("this month your student practiced X, struggled with Y, mastered Z") | P0 | yes — Premium differentiator per persona #7 |

### 3.5 Commercial infrastructure

| ID | Title | Priority | Blocker? |
|---|---|---|---|
| PRR-330 | Unit-economics dashboard: per-tier contribution margin, ARPU, LTV, churn; weekly automated report | P0 | yes |
| PRR-331 | Downgrade / churn reason-capture workflow | P0 | yes |
| PRR-332 | A/B testing harness for pricing-page variants + Plus feature-mix (90-day discovery phase post-launch) | P0 | yes |
| PRR-333 | Consumer-protection compliance: Israel Consumer Protection Law (right-of-cancellation 14 days, clear refund terms, no auto-renewal traps) | P0 | **legal gate** |

### 3.6 B2B school SKU (deferred to launch+1)

| ID | Title | Priority | Blocker? |
|---|---|---|---|
| PRR-340 | School SKU plan definition (~₪35/student/mo), classroom admin dashboard, teacher-assigned practice, SSO | P1 | — |
| PRR-341 | B2B contract template + volume pricing brackets | P1 | — |
| PRR-342 | SSO (SAML / Google Workspace for Education / Microsoft) | P1 | — |
| PRR-343 | Feature-fencing: parent dashboard does NOT appear in school SKU; tutor-handoff PDF does NOT appear | P1 | — |

### 3.7 Deferred to launch+1 (intentionally)

- Haredi "family plan" SKU (3+ students flat-rate) — evaluate after retail launch data
- Credits / à-la-carte purchases on top of subscription — if data shows heavy-user upsell demand
- Student-pay tier (for 18+ self-paying students) — rare segment
- Trial-period variant (e.g., 7-day free trial instead of 30-day money-back) — A/B test post-launch
- Referral / friend-discount program

## 4. Non-negotiable references

- [ADR-0026](../../docs/adr/0026-llm-three-tier-routing.md) — 3-tier routing defines per-tier COGS; tier caps here flow through routing layer.
- [ADR-0048](../../docs/adr/0048-exam-prep-time-framing.md) — upsell UX banned from countdown / scarcity / loss-aversion mechanics; "soft cap hit" prompts must be positive framing.
- [`docs/engineering/shipgate.md`](../../docs/engineering/shipgate.md) — CI scanner enforces banned terms; pricing page and upgrade UX must pass.
- Memory "No stubs — production grade" — billing engine must be real, no placeholder Stripe-only stub ahead of Bit/PayBox.
- Memory "Labels match data" — pricing page tier names must describe actual feature sets; no marketing-bait-switch.
- Memory "Honest not complimentary" — unit-economics dashboard shows real margins with CIs, not aspirational numbers.
- Memory "Math always LTR" — Arabic/Hebrew pricing page preserves LTR numerals in price display.
- Israel Consumer Protection Law — 14-day right-of-cancellation, VAT-inclusive display, Hebrew tax invoices.
- Israel VAT regulation — 17% current rate, inclusive of sticker price for consumers.

## 5. Open decisions blocking sub-task creation

**Decision-holder (Shaker) resolves these before sub-task generation:**

1. **Price anchors final?** ₪79 / ₪229 / ₪249 — or run a sensitivity test before committing? Parent interview (n≈10) recommended before launch.
2. **Decoy legitimacy approved?** Plus = "unlimited photo diagnostic + Sonnet for complex, no dashboard" — is this differentiator strong enough to survive engineer-parent scrutiny?
3. **Arabic parent dashboard on the critical path?** If not ready by launch, does Premium launch with Hebrew-only and Arabic parity in week 4, or does launch wait?
4. **B2B school SKU at launch or launch+1?** Persona #8 argued launch-required to prevent pricing leaks; persona #9 argued retail-first then B2B. Tie broken by: how close is any school pilot to contract?
5. **Bit + PayBox vendor DPAs** — who owns procurement? Timeline?
6. **Tutor-handoff PDF scope** — Premium-included, or ₪20 one-time export? Persona #7 implied included-as-differentiator.
7. **Annual prepay discount depth** — 10 months for 12 (17% off) recommended; or shallower (e.g., 11 for 12 ≈ 8% off)? Trades LTV lock-in vs. gross-revenue.

Plus the cross-epic integration flag:

8. **Coupling to EPIC-PRR-B governance**: per-tier LLM routing policies must be defined there, consumed here. Who owns writing the policy table — PRR-B or PRR-I?

## 6. Vendor procurement dependencies

| Gate | Party | Status |
|---|---|---|
| Stripe Israel account + VAT config | Stripe + Cena finance | not started |
| Bit merchant integration + DPA | Bit (Bank Hapoalim) + Cena legal | not started |
| PayBox merchant integration + DPA | PayBox + Cena legal | not started |
| Hebrew tax invoice template | Accountant + legal | not started |
| Israel Consumer Protection Law review | Legal | not started |

Total vendor/legal critical path: **3-6 weeks** (Bit is typically the slowest — bank-owned).

## 7. Launch timeline estimate

Subject to §5 resolutions + §6 procurement:

- Engineering-only path (core 3 tiers + Stripe + sibling + soft-caps + Hebrew parent dashboard): **5-7 weeks**.
- With Bit + PayBox procurement: **+2-4 weeks critical path** (parallel to engineering).
- With Arabic parent dashboard parity: **+2 weeks** (frontend i18n + SME content review).
- With legal reviews (Consumer Protection, VAT, refund T&C): **+1-2 weeks** overlapping.
- **Net realistic: 6-10 weeks from decision-holder greenlight to Launch.**

Parallel critical paths:
1. Engineering (billing engine, pricing page, tier enforcement, parent dashboard).
2. Procurement (Stripe + Bit + PayBox + accountant).
3. Legal (Consumer Protection Law, VAT invoicing, refund terms, T&C).
4. Content (pricing page copy HE/AR/EN, email templates, legal disclosures).

## 8. Out of scope

- Crypto / USDC / stablecoin payments (no consumer demand in Israel edu market).
- Third-party discount / coupon platforms at launch.
- Multi-currency pricing (ILS-only at launch; USD/EUR post-expansion).
- In-app purchases via App Store / Google Play (web PWA direct-pay; Apple's 30% cut is unacceptable at current unit economics — per memory "PWA over Flutter").
- Affiliate / referral program at launch.
- Stripe Tax (evaluate post-launch; Israel VAT handled manually in v1 via Hebrew tax invoice flow).

## 9. Reporting

Epic-level progress tracked via sub-task complete calls. No direct `complete` on the epic — it closes when:
1. All §3.1-§3.5 sub-tasks close.
2. All §5 decisions resolved.
3. First 100 paid subscribers processed end-to-end without billing issues.
4. Unit-economics dashboard shows actual per-tier margin for 30 consecutive days.

## 10. Success criteria (post-launch)

Measured at day 90:

- **Tier mix**: at least 40% conversions to Plus or Premium (decoy working). If <20% to Premium, restructure Plus immediately.
- **Premium churn**: <8%/mo monthly (acceptable for seasonal ed product).
- **Contribution margin per subscriber**: ≥$20/mo blended across all tiers.
- **Soft-cap hit rate**: <5% of Premium users hit soft cap in a given month. If >15%, caps are too tight.
- **Refund rate**: <10% of new subscribers request refund in 30-day window. If higher, value-proof artifacts (#9 in §2) are not landing.
- **Payment-method distribution**: ≥20% of subscribers using Bit/PayBox/bank transfer (i.e., not CC-only) — validates #10 in §2.
- **Arabic Premium adoption**: ≥15% of Arabic-locale signups convert to Premium (validates #3 in §2).

## 11. Related

- [EPIC-PRR-B](EPIC-PRR-B-llm-routing-governance.md) — owns per-tier LLM policy table
- [EPIC-PRR-C](EPIC-PRR-C-parent-aggregate-consent.md) — parent dashboard + consent; Premium depends on it
- [EPIC-PRR-H](EPIC-PRR-H-student-input-modalities.md) — HWR call caps map to tiers
- [EPIC-PRR-J](EPIC-PRR-J-photo-upload-cas-diagnostic-chain.md) — photo-diagnostic caps map to tiers
- [BUSINESS-MODEL-001-pricing-10-persona-review.md](../../docs/design/BUSINESS-MODEL-001-pricing-10-persona-review.md) — source
