# P0 Monetization Tasks

> **Source:** extracted-features.md (K1, K2, K5, K9)
> **Sprint Goal:** Establish freemium model with transparent pricing
> **Total Estimate:** 6-10 weeks
> **Expected Impact:** Revenue foundation for all other features

---

## MON-001: Freemium Model & Free Tier
**ROI: 9.0 | Size: M (4-6 weeks) | Depends on: GAM-003 (hearts), GAM-005 (gems)**

### Description
Define and implement the free tier experience. Free users get core functionality with daily limits, ads, and feature gates that drive premium conversion.

### Acceptance Criteria

**Free Tier Includes:**
- [ ] 3 AI tutoring sessions per day (quality threshold still applies)
- [ ] 5 camera scans per day (LRN-001)
- [ ] 5 hearts per session (GAM-003)
- [ ] Basic streaks and badges
- [ ] Knowledge graph view (read-only, no deep analytics)
- [ ] Basic SRS reviews (limited to 20 cards/day)
- [ ] Class social feed (read + post)
- [ ] Standard push notifications
- [ ] Ads: 1 interstitial ad between sessions (skippable after 5s)

**Free Tier Limits:**
- [ ] Daily session counter with clear display ("2 of 3 sessions remaining")
- [ ] Limit reached screen: "Come back tomorrow or upgrade for unlimited"
- [ ] Scan counter: "3 of 5 scans remaining"
- [ ] SRS review cap: "20 of 20 reviews done today. Upgrade for unlimited"
- [ ] Each limit screen shows premium upsell with value proposition

**Conversion Triggers:**
- [ ] Heart depletion → premium upsell (unlimited hearts)
- [ ] Session limit reached → premium upsell (unlimited sessions)
- [ ] Scan limit reached → premium upsell (unlimited scans)
- [ ] Ad interruption → "Remove ads with Premium"
- [ ] Advanced analytics locked → "Unlock with Premium"
- [ ] Offline content locked → "Download for offline with Premium"

### Subtasks
1. Usage counter service (sessions, scans, reviews per day per user)
2. Limit enforcement middleware
3. Limit display UI components (counters, progress bars)
4. Limit reached screens (4 variants: sessions, scans, hearts, reviews)
5. Ad integration (interstitial between sessions)
6. Premium upsell screens (6 trigger points)
7. A/B test framework for limit values (e.g., 3 vs 5 sessions/day)
8. Analytics: limit_reached, upsell_shown, upsell_clicked, upsell_converted

---

## MON-002: Premium Subscription
**ROI: 9.0 | Size: M (4-6 weeks) | Depends on: MON-001**

### Description
Premium tier subscription via App Store / Play Store in-app purchase. Unlocks unlimited access to all features.

### Acceptance Criteria

**Premium Includes:**
- [ ] Unlimited AI tutoring sessions
- [ ] Unlimited camera scans
- [ ] Unlimited hearts (never run out)
- [ ] Unlimited SRS reviews
- [ ] No ads
- [ ] Full offline content download
- [ ] Advanced analytics (weak areas, historical trends, predicted scores)
- [ ] Parent dashboard access (when built)
- [ ] Priority support

**Pricing:**
- [ ] Monthly: $9.99/month
- [ ] Annual: $79.99/year ($6.67/month — 33% savings, highlighted)
- [ ] Annual pricing shown as default (monthly as secondary option)
- [ ] "Save 33%" badge on annual plan

**Purchase Flow:**
- [ ] Premium features page (all benefits listed with checkmarks)
- [ ] Pricing comparison (free vs premium side-by-side)
- [ ] App Store / Play Store native purchase integration
- [ ] Purchase confirmation + welcome screen
- [ ] Restore purchases button (for reinstalls)
- [ ] Subscription management (link to App Store settings)
- [ ] Cancellation: shows what they'll lose, offers annual discount

**Billing Transparency (anti-SmartyMe):**
- [ ] Clear pricing on every upsell screen
- [ ] No hidden auto-renewal traps
- [ ] Cancellation instructions prominent in settings
- [ ] Confirmation email with renewal date and cancel link
- [ ] 48-hour warning before renewal (push notification)

### Subtasks
1. App Store / Play Store IAP integration (RevenueCat or native)
2. Subscription state management (active, expired, trial, grace period)
3. Premium features page UI
4. Pricing comparison screen
5. Purchase flow (select plan → confirm → welcome)
6. Restore purchases flow
7. Subscription management in settings
8. Cancellation flow with retention offer
9. Renewal reminder notification (48h before)
10. Premium badge / indicator throughout app
11. Server-side receipt validation
12. Analytics: subscription_started, subscription_renewed, subscription_cancelled, trial_started, trial_converted

---

## MON-003: Free Trial (7-Day, No Credit Card)
**ROI: 7.0 | Size: S (2-3 weeks) | Depends on: MON-002**

### Description
7-day free trial of Premium. No credit card required upfront (unlike SmartyMe's predatory model). Trial converts to free tier after expiry with option to subscribe.

### Acceptance Criteria
- [ ] 7-day full Premium access on signup (automatic)
- [ ] No credit card required during trial
- [ ] Trial countdown visible in app ("5 days left of Premium trial")
- [ ] Day 5: push notification "Your trial ends in 2 days"
- [ ] Day 7: trial expires, graceful downgrade to free tier
- [ ] Expiry screen: "Your trial ended. Here's what you'll miss:" (list features)
- [ ] Special trial-to-premium discount: first month $4.99 (50% off)
- [ ] Trial available once per account (prevent abuse)
- [ ] Trial status synced with backend (not client-side exploitable)

### Subtasks
1. Trial state model (start_date, end_date, converted, expired)
2. Auto-activate trial on account creation
3. Trial countdown UI component
4. Trial expiry notification schedule (Day 3, 5, 6, 7)
5. Graceful downgrade flow (Premium → Free)
6. Trial expiry screen with conversion offer
7. Trial-to-premium discount pricing
8. Abuse prevention (1 trial per device + account)
9. Analytics: trial_started, trial_day_N_active, trial_expired, trial_converted

---

## MON-004: Transparent Billing & Trust
**ROI: 8.0 | Size: S (1-2 weeks) | Depends on: MON-002**

### Description
Billing transparency as a competitive advantage. SmartyMe (2.3 Trustpilot) and Nibble both have predatory billing reputations. CENA should be the trusted alternative.

### Acceptance Criteria
- [ ] Pricing visible everywhere (no hidden costs)
- [ ] "Cancel anytime" messaging on all subscription screens
- [ ] 1-tap cancellation in app settings (links to platform settings)
- [ ] Cancellation confirmation: "Are you sure? You'll keep Premium until [date]"
- [ ] Post-cancellation: retain access until period ends (no immediate cutoff)
- [ ] Renewal reminder: push notification 48 hours before charge
- [ ] Receipt email after every charge with clear itemization
- [ ] Refund policy link in settings
- [ ] "Manage Subscription" button prominent in settings (not buried)
- [ ] No dark patterns: no confusing language, no hidden checkboxes, no countdown timers

### Subtasks
1. Cancel flow UI (clear, 1-tap to platform settings)
2. Renewal reminder notification (48h before)
3. Post-cancellation grace period logic
4. Receipt email template
5. Refund policy page
6. Settings page: subscription section with manage/cancel/restore
7. Copy review: audit all billing-related text for clarity
