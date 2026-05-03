# PP-002: Legal Actions Required Before Pilot

> **Status**: Engineering content complete. Awaiting legal counsel engagement.
> **Created**: 2026-04-13
> **Blocking**: Any pilot with real students / any processing of real minor data

---

## What Engineering Did

All 8 compliance documents under `docs/compliance/` have been filled with substantive technical content derived from the actual codebase. Every processing activity, data field, retention period, and consent flow described in these documents is backed by real code with file references.

The DPIA (`dpia-2026-04.md`) was already a 457-line document. The other 8 went from 33-line skeletons to 220-370 lines each. Total: ~3,000 lines of compliance documentation.

Sections requiring legal input are marked with `> **LEGAL REVIEW REQUIRED**:` blockquotes throughout.

---

## Human Actions (Priority Order)

### 1. Appoint a Privacy Protection Officer (PPO) — BLOCKING

- **Document**: `docs/compliance/ppo-appointment.md`
- **Why**: Israel PPL requires a PPO before processing sensitive data (children's education data qualifies)
- **Who**: A person with Israeli data protection expertise (Israeli bar admission or equivalent)
- **Action**: Fill in name, date, qualifications in the appointment template
- **Blocks**: DPO sign-off on DPIA, all other legal review

### 2. Engage Privacy Counsel with Israeli Law Expertise

- **Scope**: Review all 9 compliance documents (DPIA + 8 artifacts)
- **Expertise needed**: Israel Privacy Protection Law (Amendment 13), COPPA, GDPR Art 8, Israeli Education Ministry data handling
- **Specific questions counsel must answer**:

| # | Question | Document | Section |
|---|----------|----------|---------|
| Q1 | Which age verification method is compliant for Israeli schools? (self-declaration / teacher attestation / document) | age-assurance.md | Age Verification Method |
| Q2 | Is Cena a "processor" or "joint controller" with schools under Israeli law? | classroom-consumer-split.md | Controller/Processor |
| Q3 | Does the FERPA school-official exception apply to Israeli schools? | classroom-consumer-split.md | School-Directed |
| Q4 | Is 7-year student record retention appropriate under Israeli PPL? (FERPA is US-specific) | data-retention.md | Retention Schedule |
| Q5 | Is 90-day AI conversation retention sufficient under Israeli PPL? | data-retention.md | Tutor Conversations |
| Q6 | Is Anthropic's API no-training policy sufficient, or do we need a DPA training-prohibition clause? | ml-training-prohibition.md | Anthropic Policy |
| Q7 | Does the parental consent mechanism need email verification, signed form, or both? | parental-consent.md | Consent Mechanism |
| Q8 | What is the consent revocation timing requirement under Israeli law? | parental-consent.md | Revocation |
| Q9 | Is an EU representative needed if Israeli students access from EU? | privacy-notice.md | EU Representative |
| Q10 | Can consent for AdaptiveRecommendation default to ON for minors (it's the core product purpose)? | privacy-notice.md | Minor Defaults |

### 3. DPO Sign-off on DPIA

- **Document**: `docs/compliance/dpia-2026-04.md` (Section 7)
- **Who**: The appointed PPO/DPO
- **Action**: Review risk register (12 risks), mitigation register (13 mitigations), sign off on recommended decision
- **Decision options**: proceed with mitigations / proceed with conditions / prior consultation with PPA / do not proceed

### 4. Translate Privacy Notices

- **Documents**: `docs/compliance/privacy-notice.md`
- **Languages**: Hebrew, Arabic, English
- **Requirement**: Legal translator (not machine translation) per ICO Children's Code Std 4
- **Additional**: Children's version in age-appropriate language

### 5. Execute Processor Agreements

| Processor | Service | DPA Status | Action |
|-----------|---------|------------|--------|
| Anthropic | AI tutoring (Claude API) | NOT EXECUTED | Execute DPA with SCCs; add training-prohibition clause (per Q6) |
| Google/Firebase | Authentication | NOT EXECUTED | Review Google Cloud DPA terms; confirm adequacy for children's data |

### 6. Create Consent Delegation Template for Schools

- **Document**: `docs/compliance/classroom-consumer-split.md`
- **Action**: Draft a template agreement for schools that wish to delegate parental consent to the school under the school-official exception (if applicable per Q3)

---

## Document Status After Engineering

| Document | Lines | Technical | Legal Review | Translation |
|----------|-------|-----------|-------------|-------------|
| dpia-2026-04.md | 457 | Done | Needed | N/A |
| ropa.md | 222 | Done | Needed | N/A |
| age-assurance.md | 318 | Done | Needed (Q1) | N/A |
| parental-consent.md | 367 | Done | Needed (Q7, Q8) | Hebrew/Arabic forms |
| privacy-notice.md | 344 | Done | Needed (Q9, Q10) | Hebrew/Arabic/English |
| data-retention.md | 344 | Done | Needed (Q4, Q5) | N/A |
| ml-training-prohibition.md | 275 | Done | Needed (Q6) | N/A |
| classroom-consumer-split.md | 298 | Done | Needed (Q2, Q3) | N/A |
| ppo-appointment.md | 302 | Done (template) | All content legal | N/A |

---

## Acceptance Criteria Tracker (PP-002)

| # | Criterion | Status |
|---|-----------|--------|
| 1 | All 10 docs contain actual legal content, signed/reviewed by counsel | **PARTIAL** — technical content done, legal review pending |
| 2 | DPIA has DPO signature and risk assessment matrix | **PARTIAL** — risk matrix done, DPO signature pending |
| 3 | Parental consent flow legally reviewed and approved | **PENDING** — flow documented, needs counsel sign-off |
| 4 | Privacy notices in 3 languages | **PENDING** — English draft done, Hebrew/Arabic translation needed |
| 5 | PPO appointed (real person) | **PENDING** — template ready, appointment needed |
| 6 | Legal review letter confirming Israeli PPL compliance | **PENDING** — needs external counsel |
