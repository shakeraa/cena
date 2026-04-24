# Privacy Protection Officer (PPO) Appointment -- Cena Adaptive Learning Platform

**Status: PENDING -- appointment required before production data processing**
**Created**: 2026-04-13 (GD-005)
**Tracking**: FIND-privacy-014
**Legal basis**: Israel Protection of Privacy Law (PPL), GDPR Art 37-39, COPPA best practices
**Version**: 1.0

---

> **LEGAL REVIEW REQUIRED**: This entire document requires legal counsel review
> and organizational decision-making. The PPO appointment is a legal and
> organizational act, not an engineering decision. All substantive content
> below -- including the appointment details, qualifications, reporting
> structure, and responsibilities -- must be determined by the organization's
> leadership with legal guidance. This document provides a template framework
> only.

---

## 1. Why a PPO is Required

### 1.1 Israel Privacy Protection Law

The Israel Protection of Privacy Law (Chok Haganat Hapratiyut) requires
organizations that process sensitive personal data to appoint a
**Privacy Protection Officer** (Memune Al Haganat Hapratiyut -- PPO). The PPO
is the Israeli equivalent of the GDPR Data Protection Officer (DPO).

Cena qualifies for mandatory PPO appointment because:

1. **Cena processes children's educational data** -- classified as sensitive
   personal data under the Israel PPL
2. **Cena processes data at scale** -- multi-tenant platform serving multiple
   schools and individual students across Israel
3. **Cena uses automated profiling** -- Elo rating, BKT mastery prediction,
   and IRT ability estimation constitute automated decision-making affecting
   educational outcomes
4. **Israel PPL Amendment 13 (2024)** -- introduced enhanced protections for
   minors' personal data, reinforcing the obligation for organizations
   processing children's data

### 1.2 GDPR Article 37

If Cena processes data of EU/EEA data subjects (students in EU schools or
EU-resident families using the platform), GDPR Art 37(1) requires a DPO when:

- (a) Processing is carried out by a public authority -- **not applicable**
- (b) Core activities consist of processing operations requiring regular and
  systematic monitoring of data subjects on a large scale -- **applicable**
  (adaptive learning profiles, behavioral analytics)
- (c) Core activities consist of processing on a large scale of special
  categories of data -- **potentially applicable** (children's educational
  data is "data of a highly personal nature" per WP29 guidelines)

### 1.3 Current status

**The PPO/DPO has NOT been appointed.** This is tracked as FIND-privacy-014
and is referenced as a blocking prerequisite in:

- The DPIA (`docs/compliance/dpia-2026-04.md`, Section 7.1): "This DPIA
  cannot be finalized until the DPO (or equivalent qualified person) has
  reviewed and signed off."
- The Privacy Notice (`docs/compliance/privacy-notice.md`): Contact details
  list "PENDING APPOINTMENT"
- The DPIA sign-off conditions: FIND-privacy-014 is one of five conditions
  that MUST be met before the platform processes children's data in production

**This appointment must be completed before any production data processing begins.**

---

## 2. Appointment Details

> **LEGAL REVIEW REQUIRED**: All fields in this section must be completed by
> organizational leadership with legal counsel guidance.

| Field | Value |
|-------|-------|
| **Appointed person** | _________________ |
| **Appointment date** | _________________ |
| **Effective date** | _________________ |
| **Professional qualifications** | _________________ |
| **Relevant certifications** (e.g. CIPP/E, CIPM, CIPT) | _________________ |
| **Years of experience in data protection** | _________________ |
| **Appointing authority** (board resolution / CEO decision) | _________________ |
| **Appointment document reference** | _________________ |

### 2.1 Qualification requirements

> **LEGAL REVIEW REQUIRED**: Confirm the specific qualification requirements
> under Israel PPL for a PPO. The following are recommended minimums based on
> GDPR Art 37(5) and Israel PPL guidance.

The PPO should have:

- Expert knowledge of data protection law and practices, specifically:
  - Israel Protection of Privacy Law and regulations
  - GDPR (if processing EU/EEA data subjects' data)
  - COPPA (if processing US children's data)
  - FERPA (if serving US schools)
- Understanding of the educational technology sector and its specific privacy challenges
- Understanding of information technology and data security
- Ability to communicate with data subjects (students, parents, teachers) in
  Hebrew and English (Arabic desirable)
- No conflict of interest with the role (per GDPR Art 38(6))

### 2.2 Appointment formalities

> **LEGAL REVIEW REQUIRED**: Confirm the legal formalities required for PPO
> appointment under Israel PPL -- e.g. notification to the Privacy Protection
> Authority (PPA), registration in the database registry, board resolution.

- [ ] Board resolution or CEO formal appointment letter signed
- [ ] PPO acceptance of appointment signed
- [ ] Privacy Protection Authority (PPA) notified of appointment
- [ ] Database registration updated with PPO details (if required)
- [ ] PPO contact details published on the platform (privacy notice, website)
- [ ] Internal announcement to all staff

---

## 3. PPO Responsibilities

The PPO is responsible for the following, derived from Israel PPL, GDPR
Art 39, and the specific needs of Cena's educational platform:

### 3.1 Compliance oversight

| Responsibility | Description | Cena-specific context |
|----------------|-------------|----------------------|
| Monitor compliance | Ensure all data processing complies with Israel PPL, GDPR, COPPA, FERPA | Review `ProcessingPurpose.cs` purposes, consent flows, retention policies |
| DPIA review and sign-off | Review and approve DPIAs before processing begins | Sign off on `docs/compliance/dpia-2026-04.md` and quarterly updates |
| Policy review | Review and approve privacy policies, notices, and internal procedures | Review privacy notice, data retention policy, ML training prohibition |
| Training | Ensure staff processing personal data receive appropriate training | Engineering team, support staff, any staff with database access |
| Audit | Conduct or commission regular privacy audits | Review `RetentionWorker` logs, consent records, access logs |

### 3.2 Data Subject Access Requests (DSARs)

| Responsibility | Description | Cena-specific context |
|----------------|-------------|----------------------|
| Receive DSARs | Act as the point of contact for all data subject requests | Access (Art 15), rectification (Art 16), erasure (Art 17), portability (Art 20), objection (Art 21) |
| Coordinate responses | Ensure DSARs are responded to within statutory timelines | 30 days under GDPR; confirm Israel PPL timeline |
| Verify identity | Confirm the identity of the requesting data subject or parent | Parental verification for minors' requests |
| Oversee portability exports | Ensure `StudentDataExporter` produces complete and accurate exports | Review `StudentDataExporter.cs` output format and completeness |
| Oversee erasure | Ensure `RightToErasureService` fully deletes all personal data | Track FIND-privacy-005 (complete erasure implementation) |

### 3.3 Liaison with authorities

| Responsibility | Description |
|----------------|-------------|
| PPA liaison | Primary contact for the Israel Privacy Protection Authority |
| EU supervisory authority liaison | Contact point for any EU Data Protection Authority inquiries (if processing EU data) |
| FTC liaison | Contact point for FTC inquiries regarding COPPA compliance (if processing US children's data) |
| Incident notification | Notify relevant authorities of personal data breaches within statutory timelines (72 hours under GDPR Art 33; confirm Israel PPL timeline) |

### 3.4 Processor oversight

| Responsibility | Description | Cena-specific context |
|----------------|-------------|----------------------|
| Review processor agreements | Ensure DPAs are in place with all processors | Anthropic DPA (PENDING), Google/Firebase DPA (PENDING) -- tracked in DPIA Section 8 |
| Monitor processor compliance | Verify processors comply with their contractual obligations | Verify Anthropic's no-training commitment; verify Firebase data handling |
| Cross-border transfer oversight | Ensure adequate safeguards for international data transfers | SCCs with Anthropic (US); Google Cloud transfer mechanisms |

### 3.5 Cena-specific responsibilities

| Responsibility | Description |
|----------------|-------------|
| ADR-0003 enforcement | Ensure misconception data remains session-scoped per ADR-0003; no ML training on student data |
| Ship-gate review | Review allowlist exceptions in `scripts/shipgate/allowlist.json` for dark-pattern ban compliance |
| Consent purpose review | Approve any new `ProcessingPurpose` additions before they are deployed |
| Minor protection review | Review all features affecting minors before launch; ensure ICO Children's Code compliance |
| Retention audit | Verify `RetentionWorker` is running correctly and purging data per schedule |

---

## 4. Reporting Structure

> **LEGAL REVIEW REQUIRED**: The reporting structure must ensure PPO
> independence per GDPR Art 38(3) -- the PPO shall not receive any
> instructions regarding the exercise of their tasks and shall not be
> dismissed or penalized for performing their tasks. Confirm equivalent
> requirements under Israel PPL.

### 4.1 Independence requirements

The PPO must be independent of the engineering and product teams. Specifically:

- The PPO **reports directly to the board of directors or CEO** -- not to the
  CTO, VP Engineering, or any technical leadership
- The PPO **cannot be instructed** on how to handle DSARs, compliance
  assessments, or authority liaison
- The PPO **cannot be dismissed or penalized** for performing their duties
- The PPO **may hold other roles** only if there is no conflict of interest
  (GDPR Art 38(6)) -- e.g. the PPO should not also be the CTO or lead developer

### 4.2 Proposed reporting structure

```
Board of Directors / CEO
         |
         v
   Privacy Protection Officer (PPO)
         |
         +--- Direct access to board for privacy matters
         +--- Independent budget for privacy tools and external counsel
         |
   Engineering Team (separate reporting line)
         |
         +--- CTO / VP Engineering
         +--- Development teams
         +--- DevOps / Infrastructure
```

> **LEGAL REVIEW REQUIRED**: Confirm this reporting structure satisfies Israel
> PPL requirements. For small organizations, the PPO may be an external
> consultant rather than a full-time employee -- confirm whether this is
> acceptable under Israel PPL for an organization processing children's
> educational data at Cena's scale.

---

## 5. Contact Information for Data Subjects

> **LEGAL REVIEW REQUIRED**: All contact details must be confirmed and
> published before production launch.

| Channel | Details |
|---------|---------|
| **PPO name** | PENDING APPOINTMENT |
| **Email** | [privacy@cena.edu] (to be confirmed) |
| **Postal address** | _________________ |
| **Phone** | _________________ (to be confirmed; consider whether a dedicated privacy line is needed) |
| **In-app contact** | Privacy settings page with "Contact our Privacy Officer" link |
| **Response time commitment** | Within 30 days of receipt (GDPR Art 12(3)) |
| **Languages** | Hebrew, English, Arabic |

Contact details must be:

- Published in the privacy notice (`docs/compliance/privacy-notice.md`)
- Displayed in the platform's privacy settings page
- Included in the DPIA (`docs/compliance/dpia-2026-04.md`)
- Registered with the Israel Privacy Protection Authority (if required)
- Communicated to all schools and institutes using the platform

---

## 6. Blocking Dependencies

The PPO appointment (FIND-privacy-014) blocks the following:

| Blocked item | Reason |
|-------------|--------|
| DPIA finalization (`dpia-2026-04.md` Section 7.1) | DPO/PPO sign-off required |
| Production launch with children's data | PPO must be in place before processing sensitive data |
| Privacy notice publication | Contact details require PPO appointment |
| Processor agreement execution (Anthropic DPA) | PPO should review and approve |
| Parental consent flow launch (FIND-privacy-001) | PPO should review consent mechanisms |

---

## 7. Implementation Checklist

> **LEGAL REVIEW REQUIRED**: This entire checklist must be reviewed and
> completed under legal guidance.

### Pre-appointment

- [ ] Identify candidate(s) with required qualifications (Section 2.1)
- [ ] Confirm appointment formalities under Israel PPL (Section 2.2)
- [ ] Determine whether internal appointment or external consultant
- [ ] Allocate budget for PPO function (tools, external counsel, training)
- [ ] Draft appointment letter and terms of reference

### Appointment

- [ ] Board resolution or CEO formal appointment
- [ ] PPO acceptance
- [ ] PPA notification (if required)
- [ ] Database registration update (if required)

### Post-appointment

- [ ] Publish contact details in privacy notice
- [ ] Update DPIA with PPO details and obtain sign-off
- [ ] Update platform privacy settings page with PPO contact
- [ ] Notify all partner schools and institutes
- [ ] PPO onboarding: review codebase compliance artifacts (this directory)
- [ ] PPO review of all pending privacy findings (FIND-privacy-001 through FIND-privacy-014)
- [ ] PPO review and sign-off on processor agreements (Anthropic, Google)
- [ ] Establish DSAR handling procedures
- [ ] Establish data breach notification procedures
- [ ] Schedule first quarterly compliance review

---

## Review History

| Date | Reviewer | Notes |
|------|----------|-------|
| 2026-04-13 | claude-code | Skeleton created (GD-005) |
| 2026-04-13 | claude-code | Substantive rewrite with codebase-derived content |
