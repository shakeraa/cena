# EPIC-E2E-E — Parent console (digest, consent, dashboard, controls)

**Status**: Proposed
**Priority**: P1 (trust / legal surface — parents must see what we say they see, nothing more)
**Related ADRs**: [ADR-0041](../../docs/adr/0041-parent-auth-role-age-bands.md), [ADR-0042](../../docs/adr/0042-consent-aggregate-bounded-context.md), [ADR-0038](../../docs/adr/0038-event-sourced-right-to-be-forgotten.md), [EPIC-PRR-C](../pre-release-review/EPIC-PRR-C-parent-aggregate-consent.md)

---

## Why this exists

Parent surfaces span three hostile edges at once: COPPA (< 13), GDPR-K (EU kids), and Ministry-of-Education (Israel). Every parent-visible data point must be justifiable in all three. Every consent flip must be event-sourced + replayable.

## Workflows

### E2E-E-01 — Parent digest email → delivered → click-through → dashboard

**Journey**: child completes a week of practice → Saturday 08:00 local → `ParentDigestScheduler` builds digest → SMTP sender delivers email → parent clicks magic link → `/parent/dashboard` shows week summary with child's metrics filtered by age-band visibility (ADR-0041).

**Boundaries**: DOM (dashboard renders metrics, no over-14 fields shown per age-band), SMTP (email captured via emulated SMTP sink in test-mode), DB (DigestDeliveredV1 event), bus (`ParentDigestDeliveredV1`).

**Regression caught**: dashboard shows mastery numbers for a 13+ child (should be hidden per prr-052); magic link signs in wrong parent (token-scope bug); digest sent when parent opted out.

### E2E-E-02 — Parent digest — WhatsApp path (Meta direct, PRR-429)

**Journey**: parent opted into WhatsApp channel → digest-cadence scheduler triggers → `IWhatsAppSender` (meta backend) sends templated message → parent receives → reply triggers no-op (inbound webhook PRR-430).

**Boundaries**: outbound Meta API call captured with correct `tenant_id` metadata, DB (delivery row), bus (`ParentDigestDeliveredV1` with `channel=whatsapp`).

**Regression caught**: WhatsApp sent when opt-in is email-only; marketing-template sent when utility-template expected; cross-family template mix-up.

### E2E-E-03 — Unsubscribe one-click link (prr-051)

**Journey**: digest email → parent clicks unsubscribe → token-authenticated anonymous endpoint → preferences flipped to all-off → confirmation page → next digest cycle: no email/WhatsApp.

**Boundaries**: DOM (confirmation page + success state), DB (IParentDigestPreferencesStore reflects opt-out), bus (`ParentDigestPreferencesChangedV1`), token single-use (second click → 409).

**Regression caught**: unsubscribe silently fails; token reusable (could be replayed maliciously); cascade to both email + WhatsApp missed.

### E2E-E-04 — Consent flow (ADR-0042 ConsentAggregate)

**Journey**: new parent registers → sees consent dialog (observability, AI tutoring, marketing — each separately) → flips toggles → `ConsentAggregate` appends one event per toggle → read-model reflects new state → admin consent-audit export (prr-130) shows the full trail.

**Boundaries**: DOM (toggle states persist across reload), DB (ConsentEventV1 rows event-sourced, not mutated in place), bus (`ConsentGrantedV1`, `ConsentRevokedV1`), admin CSV export has the timestamped audit trail.

**Regression caught**: toggle revoked but state says granted; consent applied to wrong child (parent with 2 kids); audit export missing a row.

### E2E-E-05 — Accommodations profile (RDY-066 / PRR-C)

**Journey**: parent goes to `/parent/accommodations` → configures profile (extra time, font size, hide-reveal) → signs consent-doc → saves → next session: child's session respects the profile.

**Boundaries**: DOM (profile UI + session UI honors settings), DB (`AccommodationProfileAssignedV1` event with consent-doc hash), LearningSession actor reads profile on session-start.

**Regression caught**: accommodation not applied to next session; wrong-child's profile applied; consent-doc hash mismatch silently ignored.

### E2E-E-06 — Time-budget control (prr-077)

**Journey**: parent sets 30-min daily cap → child's session approaches cap → UI warns, doesn't lock (soft cap per prr-077) → session continues but parent sees alert.

**Boundaries**: DOM (student-side warning at 80% + 100%), DB (`ParentalControlsConfiguredV1`), ship-gate check (no hard lockout; no dark-pattern pressure copy).

**Regression caught**: hard lockout added (ship-gate violation); warning threshold moves dates (time-aware OK, time-pressure not); parent alert drops.

### E2E-E-07 — Dashboard-visibility age-band filter (prr-052)

**Journey**: parent of a 14-year-old logs in → dashboard shows ONLY the fields allowed for 14+ (no mastery breakdown, no misconception patterns — GDPR-K) → parent of a 9-year-old sees the wider set.

**Boundaries**: DOM rendered field list equals backend `/api/parent/dashboard-visibility` response; fields filtered per age band.

**Regression caught**: filter leaks fields; backend filter differs from frontend filter (consistency drift); field set not updated when child ages into a new band.

### E2E-E-08 — Right-to-be-forgotten cascade (ADR-0038)

**Journey**: parent requests child deletion → `IRightToErasureService` runs → ConsentAggregate + StudentProfile + DigestPreferences + WhatsAppRecipient all crypto-shredded → manifest shows "Preserved via ADR-0038 crypto-shred" → 90-day audit window starts.

**Boundaries**: DB (all personal rows shredded, aggregates preserved as opaque tombstones), manifest file contents, bus (`RightToErasureCompletedV1`).

**Regression caught**: orphan row left behind (partial erasure); manifest missing a cascade; event-stream leaks the original child id post-shred.

## Out of scope

- Teacher-side classroom analytics — EPIC-E2E-F
- Admin consent-override flow (prr-096) — EPIC-E2E-G

## Definition of Done

- [ ] 8 workflows green
- [ ] E-08 (RTBF) double-asserted: DB scan for personal fields AFTER erasure must be empty
- [ ] E-04 (consent), E-08 tagged `@gdpr @p0` — blocks merge if red
- [ ] WhatsApp path (E-02) uses MetaCloud in-memory mock when Stripe CLI unavailable (CI-tier dev stack)
- [ ] Digest cadence tests freeze wall clock via IClock test-seam
