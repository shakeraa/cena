# LCM-004: Plan Management — Subscription Enforcement, Billing Webhooks, Grace Period

**Priority:** P1 — revenue enablement
**Blocked by:** LCM-001 (Actor Status Gate)
**Estimated effort:** 4 days
**Phase:** 4

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic. See `tasks/00-master-plan.md` for the full rule.

## Context

The `plan` claim exists in Firebase custom claims but is not enforced anywhere. Students can use all features regardless of plan. Need: billing webhook integration, plan expiry with 7-day grace period, feature gating at API and actor levels, cancellation with end-of-period access.

Plan tiers: `free` (3 questions/day, no tutoring), `basic` (unlimited questions, no AI tutoring), `premium` (full features), `school` (school-managed, full features).

## Subtasks

### LCM-004.1: Plan Data Model

**Files to modify:**
- `src/shared/Cena.Infrastructure/Documents/AdminUser.cs` — add plan fields

**Acceptance:**
- [ ] Add `PlanId` (string) — `free`, `basic`, `premium`, `school`
- [ ] Add `PlanExpiresAt` (DateTimeOffset?)
- [ ] Add `PlanCancelledAt` (DateTimeOffset?) — user cancelled, access until period end
- [ ] Add `PlanEndsAt` (DateTimeOffset?) — when access actually downgrades
- [ ] Add `GracePeriodEndsAt` (DateTimeOffset?) — 7 days after expiry

### LCM-004.2: Plan Enforcement Service

**Files to create:**
- `src/shared/Cena.Infrastructure/Plans/PlanEnforcementService.cs` — `IPlanEnforcementService`
- `src/shared/Cena.Infrastructure/Plans/PlanFeatures.cs` — feature matrix per tier

**Acceptance:**
- [ ] `GetEffectivePlan(uid)` — returns current plan considering expiry, grace, cancellation
- [ ] `CanAccess(uid, feature)` — checks plan tier against feature matrix
- [ ] Feature matrix:
  - `free`: 3 questions/day, no tutoring, no AI hints, no data export
  - `basic`: unlimited questions, basic hints, no AI tutoring
  - `premium`: all features
  - `school`: all features (school-managed)
- [ ] Grace period: 7 days after expiry, read-only access (can review past work, no new sessions)
- [ ] Redis cache: `plan:{uid}` with 5-min TTL for fast lookups

### LCM-004.3: Plan Enforcement Middleware

**Files to create:**
- `src/shared/Cena.Infrastructure/Plans/PlanEnforcementMiddleware.cs`

**Acceptance:**
- [ ] Runs after auth middleware, before endpoint routing
- [ ] Checks `IPlanEnforcementService.GetEffectivePlan()` for current user
- [ ] If expired (past grace): set response header `X-Plan-Status: expired`
- [ ] If in grace: set header `X-Plan-Status: grace`, `X-Grace-Expires: {date}`
- [ ] Feature-gated endpoints return 402 Payment Required with plan upgrade info
- [ ] Non-gated endpoints (auth, profile, plan status) always allowed

### LCM-004.4: Actor-Level Plan Enforcement

**Files to modify:**
- `src/actors/Cena.Actors/Students/StudentState.cs` — add `PlanTier` field
- `src/actors/Cena.Actors/Students/StudentActor.Commands.cs` — check plan on `StartSession`, `AttemptConcept`

**Acceptance:**
- [ ] `StudentState.PlanTier` synced from NATS event `cena.account.plan_changed`
- [ ] `StartSession` rejected if plan expired (past grace)
- [ ] `AttemptConcept` applies daily question limit for free tier
- [ ] `RequestHint` checks plan tier for AI hint eligibility
- [ ] Tutoring commands rejected for free/basic plans

### LCM-004.5: Billing Webhook Handler

**Files to create:**
- `src/api/Cena.Admin.Api/BillingWebhookHandler.cs`
- `src/api/Cena.Admin.Api/BillingWebhookEndpoints.cs`

**Endpoints:**
- `POST /api/webhooks/billing` — Stripe/billing provider webhook

**Acceptance:**
- [ ] Webhook signature verification (Stripe-Signature header)
- [ ] Handle events: `subscription.created`, `subscription.updated`, `subscription.cancelled`, `invoice.paid`, `invoice.payment_failed`
- [ ] On payment success: update plan, extend expiry, publish `cena.account.plan_changed`
- [ ] On payment failure: set grace period start, notify user
- [ ] On cancellation: set `PlanCancelledAt`, `PlanEndsAt` = current period end
- [ ] Idempotency: store processed webhook IDs in Redis (24h TTL)

### LCM-004.6: Plan Expiry Cron Job

**Files to create:**
- `src/actors/Cena.Actors.Host/Jobs/PlanExpiryJob.cs`

**Acceptance:**
- [ ] Runs daily at 04:00 UTC
- [ ] Finds users where `PlanExpiresAt <= now` and `GracePeriodEndsAt > now` → set status to `Grace`
- [ ] Finds users where `GracePeriodEndsAt <= now` → downgrade to `free`, publish NATS event
- [ ] Finds users where `PlanEndsAt <= now` (cancelled) → downgrade to `free`

### LCM-004.7: Flutter — Plan Status UI

**Files to create:**
- Flutter: `lib/features/settings/subscription_screen.dart`

**Acceptance:**
- [ ] Current plan display with features list
- [ ] Expiry warning banner (30/14/7/1 day before)
- [ ] Grace mode: "Your plan expired. Renew to continue learning" banner
- [ ] Free tier: feature badges showing locked features
- [ ] Cancel subscription with end-of-period confirmation
- [ ] Resubscribe button

### LCM-004.8: Admin Dashboard — Plan Management

**Files to modify:**
- `src/admin/full-version/src/views/apps/user/UserTabBillingsPlans.vue` — wire to backend

**Acceptance:**
- [ ] Show actual plan data (currently UI-only shell)
- [ ] School-level plan management: bulk assign plans
- [ ] Individual plan override (admin extends/upgrades)
- [ ] Plan status visible in user list (badge: free/basic/premium/school)
- [ ] Grace period countdown on user cards

### LCM-004.9: Tests

**Acceptance:**
- [ ] Test: free tier enforces 3 questions/day
- [ ] Test: grace period allows read-only access
- [ ] Test: expired plan (past grace) blocks new sessions
- [ ] Test: billing webhook updates plan correctly
- [ ] Test: plan cancellation grants access until period end
- [ ] Test: actor rejects commands for expired plans
