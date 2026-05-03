# PP-002: Complete Compliance Documentation with Legal Counsel (CRITICAL)

- **Priority**: Critical — legal blocker for any pilot with real students
- **Complexity**: Architect level — requires privacy lawyer with Israeli law expertise
- **Blocks**: Any pilot involving minors, any processing of real student data
- **Source**: Expert panel review § Compliance & Privacy (Dr. Rami)

## Problem

The 10 compliance documents under `docs/compliance/` are structural skeletons with section headers, placeholder guidance notes, and TODO markers. None contain the actual legal content required for regulatory compliance. The platform targets minors in Israel, which triggers:

- Israel Privacy Protection Law (Amendment 13) — applies to all users in Israel
- COPPA (if any US users exist) — mandatory for under-13
- GDPR Article 8 (if any EU users exist) — parental consent for under-16
- Israeli Education Ministry data handling requirements

## Documents Requiring Legal Completion

| File | Status | What's Missing |
|------|--------|---------------|
| `docs/compliance/dpia-2026-04.md` | Skeleton | Full DPIA assessment, DPO signature, risk matrix |
| `docs/compliance/ropa.md` | Skeleton | Complete record of processing activities per GDPR Art. 30 |
| `docs/compliance/age-assurance.md` | Skeleton | Specific age verification method, consent flow for under-16 |
| `docs/compliance/parental-consent.md` | Skeleton | Consent mechanism, revocation procedure, consent record storage |
| `docs/compliance/privacy-notice.md` | Skeleton | Full privacy notice in Hebrew, Arabic, and English |
| `docs/compliance/data-retention.md` | Skeleton | Retention schedule per data category with legal justification |
| `docs/compliance/ferpa-agreement.md` | Skeleton | FERPA school official agreement template (if US schools involved) |
| `docs/compliance/ml-training-prohibition.md` | Skeleton | Enforceable policy that student data is excluded from ML training |
| `docs/compliance/classroom-consumer-split.md` | Skeleton | Legal distinction between classroom (school-directed) and consumer (parent-directed) use |
| `docs/compliance/ppo-appointment.md` | Skeleton | Privacy Protection Officer appointment letter per Israeli law |

## Deliverables

1. Engage a privacy lawyer with Israeli data protection and education law expertise
2. Complete each document with actual legal content, not templates
3. DPIA must be signed by a qualified Data Protection Officer
4. Parental consent mechanism must be reviewed for compliance with Israeli Education Ministry requirements
5. Privacy notices must be translated into Hebrew, Arabic, and English by a legal translator (not machine translation)
6. The PPO appointment must be a real person, not a role description

## Acceptance Criteria

- [ ] All 10 compliance documents contain actual legal content, signed/reviewed by qualified counsel
- [ ] DPIA has DPO signature and risk assessment matrix
- [ ] Parental consent flow is legally reviewed and approved
- [ ] Privacy notices exist in 3 languages (Hebrew, Arabic, English)
- [ ] PPO is appointed (real person with Israeli bar admission or equivalent qualification)
- [ ] Legal review letter/opinion on file confirming compliance with Israeli Privacy Protection Law

## Out of Scope

- Technical implementation of consent flows (separate engineering task after legal approval)
- FERPA compliance (only needed if US schools are in scope for pilot)
