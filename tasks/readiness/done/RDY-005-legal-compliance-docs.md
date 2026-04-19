# RDY-005: Legal Compliance Documents (DPA, Privacy Notice, COPPA, Incident Response)

- **Priority**: Critical — blocks launch with real students
- **Complexity**: Legal counsel required — engineering supports with data flows
- **Source**: Expert panel audit — Ran (Security)
- **Tier**: 1 (blocks legal deployment)
- **Effort**: 8-16 weeks realistic (was 4-8 weeks), $15-30K legal cost

> **Rami's challenge**: This is NOT an engineering task — it's a legal task. Ran is listed as owner but is not a lawyer. The "4-8 weeks" estimate is false: DPA with Anthropic alone is 1 week if they have a template, 6-12 weeks if custom negotiation. COPPA form + legal review = 3-4 weeks. Incident response plan + DPO sign-off = 2 weeks. Total with sequencing: 8-16 weeks minimum.
>
> **Also duplicates PP-002** — both cover DPA, privacy notice, COPPA. RDY-005 is more comprehensive. Recommend retiring PP-002.
>
> **Immediate action**: Contact Anthropic's legal team THIS WEEK. If they have a template DPA, scope drops to 4-6 weeks. Schedule 1-hour call with general counsel to scope and budget.

## Problem

### 1. No DPA with Anthropic
GDPR Art. 28 requires a Data Processing Agreement with any third-party processor. Anthropic processes student tutor messages (PII). No DPA exists.

### 2. Privacy notice / code retention mismatch
Children's privacy notice (`docs/legal/privacy-policy-children.md`) promises "30 days" for data deletion. Code (`DataRetentionPolicy.cs`) retains tutor messages for 90 days. This inconsistency is a legal liability.

### 3. No COPPA parental consent form
COPPA requires a specific verifiable parental consent mechanism. The age gate and consent flow exist in code, but no legal form template exists.

### 4. No incident response plan
GDPR Art. 33/34 requires a breach notification plan (72 hours to authority, without undue delay to affected individuals). No plan exists.

### 5. Compliance docs are drafts
Privacy policy and ToS are marked as drafts. No legal counsel has reviewed them.

## Scope

### 1. DPA with Anthropic
- Engineering: document data flows (what PII reaches Anthropic, retention, purpose)
- Legal: negotiate and sign DPA per GDPR Art. 28 requirements
- Engineering: implement DPA-required controls (audit logging of API calls, data deletion on request)

### 2. Align privacy notice with code
- Option A: Change code to match 30-day promise (reduce tutor message retention from 90 to 30 days)
- Option B: Change privacy notice to state 90 days with explanation
- Legal decides; engineering implements

### 3. COPPA parental consent form
- Legal: draft verifiable parental consent form meeting FTC requirements
- Engineering: serve form via `POST /api/auth/parent-consent-challenge` flow
- Must include: what data is collected, how it's used, parent's rights, deletion request process

### 4. Incident response plan
- Legal + Engineering: document breach response procedure
- Include: detection → assessment → notification to Israeli Privacy Authority (72h) → notification to affected users → remediation

### 5. Legal review of existing docs
- Legal counsel reviews `docs/legal/privacy-policy-children.md` and `docs/legal/terms-of-service.md`
- Add mention of Anthropic as third-party AI processor in ToS
- Add Israeli Privacy Protection Authority complaint procedure

## Files to Modify

- `docs/legal/privacy-policy-children.md` — align retention periods, add COPPA citation
- `docs/legal/terms-of-service.md` — add AI processor disclosure, data retention
- New: `docs/legal/dpa-anthropic.md` — signed DPA (or reference to external doc)
- New: `docs/legal/parental-consent-form.md` — COPPA-compliant form
- New: `docs/legal/incident-response-plan.md` — GDPR Art. 33/34 procedure
- `src/shared/Cena.Infrastructure/Compliance/DataRetentionPolicy.cs` — if retention periods change

## Acceptance Criteria

- [ ] DPA with Anthropic signed (or documented as in-progress with timeline)
- [ ] Privacy notice retention periods match code exactly
- [ ] Parental consent form exists and is served during age-gated onboarding
- [ ] Incident response plan documented with roles, timelines, authority contacts
- [ ] All legal docs reviewed by counsel (not engineering drafts)
- [ ] ToS mentions Anthropic as third-party AI processor
- [ ] Student Data Processing Agreement (SDPA) template created for schools signing with Cena
- [ ] Teacher/staff consent forms for admin users accessing student data
- [ ] Data breach notification email template (GDPR Art. 34, Israeli Privacy Authority)
- [ ] Anthropic subprocessor list reviewed and documented
- [ ] AI transparency notice: "The AI tutor is artificial intelligence, not a human"
- [ ] Privacy notice corrected: tutor chats retained 90 days (not "12 months" as currently stated)

> **Cross-review (Ran)**: Original scope missed 7 documents. The 12-month claim in privacy notice contradicts the 90-day code retention — courts may interpret this as false marketing. Subprocessor disclosure required by GDPR Art. 28(4).
