# FOC-009: Sensor Privacy Layer (On-Device, Consent)

**Priority:** P0 — legal and ethical requirement before any sensor data collection
**Blocked by:** FOC-002 (sensor collection layer)
**Estimated effort:** 3-5 days
**Contract:** Must comply with Israeli Privacy Protection Law 5741-1981, GDPR (for EU exposure), and Apple/Google app review guidelines

---

> **NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic. See `tasks/00-master-plan.md`.

## Context

Mobile sensor data (accelerometer, touch patterns, app usage) is personal data under GDPR Art. 4(1) and Israel's Privacy Protection Law. Cena collects sensor data from MINORS (16-18 year olds) — elevated privacy requirements. The ADHD research (autoresearch Iteration 13) specifically flagged: "ethical concerns surrounding data privacy necessitate a cautious approach to implementation."

**Cena's privacy principle:** Raw sensor data NEVER leaves the device. Only aggregated scores (0.0-1.0) are transmitted to the backend.

## Subtasks

### FOC-009.1: On-Device Processing Pipeline
**Files:**
- `lib/services/sensors/sensor_privacy_pipeline.dart` — NEW

**Acceptance:**
- [ ] ALL sensor data processing happens on-device (in the Flutter app)
- [ ] Raw accelerometer/gyroscope/light/touch data is processed into scores in-memory
- [ ] Raw data is NEVER persisted to disk, NEVER transmitted via network
- [ ] Only `SensorSnapshot` (4 numeric scores + timestamp) leaves the device
- [ ] `SensorSnapshot` cannot be reverse-engineered to reconstruct raw sensor data
- [ ] Data lifecycle: raw sensor → score computation → raw sensor discarded (same frame)

### FOC-009.2: Granular Consent Management
**Files:**
- `lib/services/sensors/sensor_consent_manager.dart` — NEW
- `lib/screens/settings/sensor_permissions_screen.dart` — NEW

**Acceptance:**
- [ ] Each sensor type has independent opt-in/opt-out:
  - Motion (accelerometer/gyroscope) — default: OFF
  - App focus (lifecycle tracking) — default: ON (non-invasive)
  - Touch patterns — default: OFF
  - Environment (light/battery) — default: ON (non-invasive)
- [ ] Consent stored in `flutter_secure_storage` (encrypted)
- [ ] Parental consent required for students under 18 (Israeli law)
- [ ] Consent can be revoked at any time from Settings → Privacy → Sensor Data
- [ ] Revoking consent immediately stops collection AND deletes any locally cached scores
- [ ] Clear explanation per sensor: what it collects, why, what score looks like

### FOC-009.3: Consent Onboarding Flow
**Files:**
- `lib/screens/onboarding/sensor_consent_screen.dart` — NEW

**Acceptance:**
- [ ] During onboarding, after account creation, show sensor consent screen
- [ ] Hebrew/Arabic/English text explaining each sensor category
- [ ] Toggle per category with explanation
- [ ] "Why does Cena want this?" expandable section per sensor
- [ ] "What data leaves my phone? Only a number between 0 and 1. Never your actual sensor readings."
- [ ] "You can change this anytime in Settings"
- [ ] Skip button (all sensors OFF = app works fine with 4-signal model)

### FOC-009.4: Data Minimization Audit
**Files:**
- `lib/services/sensors/sensor_audit_log.dart` — NEW

**Acceptance:**
- [ ] Local-only audit log: tracks what sensor data was processed and when
- [ ] Viewable by user in Settings → Privacy → Sensor Activity
- [ ] Shows: "Motion sensor: 342 readings processed on-device, 0 sent to server"
- [ ] Audit log auto-expires after 30 days
- [ ] No PII in audit log (just counts and timestamps)

## Research References
- ADHD digital learning research: privacy concerns flagged
- Focus Degradation Research doc, Section 4.4 (ADHD considerations)
- Israeli Privacy Protection Law 5741-1981
- GDPR Art. 4(1), Art. 6(1)(a), Art. 8 (child consent)
