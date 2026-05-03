# Privacy Notice -- Cena Adaptive Learning Platform

**Status: DRAFT -- awaiting DPO appointment (FIND-privacy-014), legal counsel review, and translation**
**Created**: 2026-04-13 (GD-005)
**Legal basis**: GDPR Art 13/14, COPPA 16 CFR 312.4, Israel PPL Section 11, FERPA 34 CFR 99.7
**Controller**: Cena Adaptive Learning Platform (Israel)
**Version**: 1.0

---

> **LEGAL REVIEW REQUIRED**: This entire document must be reviewed by qualified
> legal counsel in Israel, the EU, and the US before publication. The full text
> must be translated to Hebrew, Arabic, and English by a certified legal
> translator. A children's version must be prepared using age-appropriate
> language per ICO Children's Code Standard 4. The children's version should be
> reviewed by child language specialists for clarity at the 8--12 age range.

---

## 1. Who We Are (Controller Identity)

Cena Adaptive Learning Platform ("Cena", "we", "us") is an adaptive learning
platform operated from Israel. We act as the **data controller** for the
personal data described in this notice.

Contact details:

| Role | Contact |
|------|---------|
| Data Protection Officer (PPO) | **PENDING APPOINTMENT** (tracked as FIND-privacy-014). Until appointment, direct privacy queries to: [privacy@cena.edu] |
| General contact | [contact@cena.edu] |

> **LEGAL REVIEW REQUIRED**: Confirm the legal entity name, registered address,
> and company registration number for the controller statement. Confirm whether
> a representative in the EU (GDPR Art 27) is required.

---

## 2. What Data We Collect

### 2.1 Account and profile data

| Field | PII Level | Category | Collected at | Source |
|-------|-----------|----------|-------------|--------|
| Email address | High | contact | Registration | User / parent |
| Display name / full name | Medium | identity | Registration | User / parent |
| Date of birth | High | identity | Registration (age-gate) | User / parent |
| Student ID (internal) | Low | identity | Auto-generated | System |
| School ID | Low | identity | Enrollment | School / system |
| Parent/guardian email | High | contact | Parental consent flow | Parent |
| Consent tier | None | system | Derived from DOB | System |

PII levels are enforced programmatically via the `[Pii]` attribute system
defined in `src/shared/Cena.Infrastructure/Compliance/PiiClassification.cs`.

### 2.2 PII classification system

Cena classifies all personal data fields using a 5-level system:

| Level | Name | Description | Encryption required | Excluded from logs |
|-------|------|-------------|--------------------|--------------------|
| 0 | **None** | Not PII -- safe for logs, exports, and LLM routing | No | No |
| 1 | **Low** | Anonymized identifiers, internal IDs (e.g. StudentId, SchoolId) | No | No |
| 2 | **Medium** | Display names, usernames (e.g. FullName) | No | Yes |
| 3 | **High** | Email, phone, DOB, parent email -- requires encryption at rest | Yes | Yes |
| 4 | **Critical** | Government IDs, payment info -- requires encryption at rest | Yes | Yes |

Code reference: `PiiLevel` enum and `PiiAttribute` class in
`src/shared/Cena.Infrastructure/Compliance/PiiClassification.cs`. The
`PiiScanner` (`src/shared/Cena.Infrastructure/Compliance/PiiScanner.cs`)
discovers annotated fields at runtime for audit, export, and log-sanitization.

### 2.3 PII field annotations on the student profile

The `StudentProfileSnapshot` (`src/actors/Cena.Actors/Events/StudentProfileSnapshot.cs`)
carries these PII annotations:

| Property | PII Level | Category | Encryption | Log exclusion |
|----------|-----------|----------|------------|---------------|
| `StudentId` | Low | identity | No | No |
| `FullName` | Medium | identity | No | Yes |
| `SchoolId` | Low | identity | No | No |
| `DateOfBirth` | High | identity | Yes | Yes |
| `ParentEmail` | High | contact | Yes | Yes |

### 2.4 Learning and behavioral data

We also collect the following categories of data during platform use:

- **Learning events**: Question responses (correct/incorrect), session start/end times, questions attempted, questions correct, subject and topic identifiers
- **AI tutor conversations**: Free-text messages exchanged with the AI tutor (Anthropic Claude), including student context (subject, mastery level) sent as system prompts
- **Focus analytics**: Session-level engagement signals (time-on-task, focus scores, session timing). We do **not** use per-keystroke tracking, webcam, or microphone data.
- **Device metadata**: Browser user-agent string, screen dimensions, IP address (anonymized within 90 days)
- **Profiling data**: Elo rating (question difficulty calibration), Bayesian Knowledge Tracing mastery probability per skill, IRT ability estimates

### 2.5 Misconception data (session-scoped only)

When the AI tutor identifies a misconception (e.g. distributing an exponent
over addition), this data is tagged to your **learning session only**. Per
[ADR-0003](../adr/0003-misconception-session-scope.md), misconception data is:

- Never stored on your permanent student profile
- Retained for a maximum of 30 days (hard cap: 90 days)
- Never used for ML model training
- Not included in GDPR data portability exports

---

## 3. Why We Process Your Data (Processing Purposes)

Cena defines 10 granular processing purposes in code
(`src/shared/Cena.Infrastructure/Compliance/ProcessingPurpose.cs`). Each
purpose has an explicit GDPR Article 6 lawful basis and age-appropriate
defaults for minors (under 16):

| # | Purpose | Description | GDPR Lawful Basis | Adult default | Minor default | Can be disabled? |
|---|---------|-------------|-------------------|---------------|---------------|------------------|
| 1 | **AccountAuth** | Account authentication -- required for account access and security | Art 6(1)(b) Contract | ON | ON | No (required) |
| 2 | **SessionContinuity** | Session management -- required to maintain your learning session | Art 6(1)(b) Contract | ON | ON | No (required) |
| 3 | **AdaptiveRecommendation** | Personalized learning recommendations tailored to your progress | Art 6(1)(a) Consent | ON | ON | Yes |
| 4 | **PeerComparison** | Compare your progress with anonymized peer groups | Art 6(1)(a) Consent | ON | OFF | Yes |
| 5 | **LeaderboardDisplay** | Display your achievements on leaderboards visible to other students | Art 6(1)(a) Consent | ON | OFF | Yes |
| 6 | **SocialFeatures** | Social features including posts, comments, and community interactions | Art 6(1)(a) Consent | ON | OFF | Yes |
| 7 | **ThirdPartyAi** | AI tutoring assistance powered by third-party services (Anthropic) | Art 6(1)(a) Consent | ON | OFF | Yes |
| 8 | **BehavioralAnalytics** | Learning analytics including focus tracking and behavior analysis | Art 6(1)(a) Consent | ON | OFF | Yes |
| 9 | **CrossTenantBenchmarking** | Cross-school benchmarking for anonymized institutional comparisons | Art 6(1)(a) Consent | OFF | OFF | Yes |
| 10 | **MarketingNudges** | Promotional notifications and feature recommendations | Art 6(1)(a) Consent | OFF | OFF | Yes |

**Key design decision**: 7 of 10 purposes default to OFF for minors. Only
AccountAuth, SessionContinuity, and AdaptiveRecommendation default to ON for
minors. Consent is managed per-student via the `GdprConsentManager`
(`src/shared/Cena.Infrastructure/Compliance/GdprConsentManager.cs`) with
full audit trail of all consent changes.

> **LEGAL REVIEW REQUIRED**: Confirm whether AdaptiveRecommendation (purpose 3)
> defaulting to ON for minors is acceptable, or whether parental consent must
> be obtained before activation. This is tracked as FIND-privacy-001. Also
> confirm whether "legitimate interest" (Art 6(1)(f)) is a valid basis for
> administrative analytics in the education context.

---

## 4. Who We Share Your Data With

### 4.1 Third-party processors

| Processor | Service | Location | Transfer mechanism | Data shared |
|-----------|---------|----------|--------------------|-------------|
| **Anthropic** (San Francisco, CA, USA) | AI tutoring (Claude API) | United States | Standard Contractual Clauses (SCCs) required | Conversation messages, student context (subject, mastery level). **Note**: `ThirdPartyAi` consent is required and defaults to OFF for minors. |
| **Google / Firebase** (Google Cloud) | Authentication (Firebase Auth) | Multi-region (EU/US) | SCCs / Adequacy decision | Email, display name, hashed credentials, authentication tokens |

### 4.2 No other third-party sharing

We do **not** share personal data with:
- Advertising networks or data brokers
- Social media platforms
- Other educational technology companies
- Government agencies (unless compelled by law)

### 4.3 Cross-border transfers

Student conversation data transmitted to Anthropic (US) constitutes a
cross-border transfer. Safeguards include:

- Standard Contractual Clauses (SCCs) -- **PENDING execution** (see DPIA Section 8)
- PII scrubbing before transmission to Anthropic (planned: FIND-privacy-008)
- Anthropic's API data use policy: API inputs are NOT used for model training

> **LEGAL REVIEW REQUIRED**: Confirm whether SCCs alone are sufficient for
> Israel-to-US transfers of children's data, or whether additional safeguards
> (e.g. supplementary technical measures, Transfer Impact Assessment) are required.
> Confirm status of Israel adequacy decisions with respect to the EU.

---

## 5. How Long We Keep Your Data (Retention Schedule)

Retention periods are defined in code at
`src/shared/Cena.Infrastructure/Compliance/DataRetentionPolicy.cs` and
enforced by the `RetentionWorker` background service
(`src/shared/Cena.Infrastructure/Compliance/RetentionWorker.cs`), which runs
on a configurable schedule (default: daily at 2:00 AM).

| Data category | Retention period | Legal basis | Code constant |
|---------------|-----------------|-------------|---------------|
| **Education records** (event streams, mastery snapshots, tutoring transcripts) | 7 years after last enrollment | FERPA requirement | `StudentRecordRetention = 365 * 7 days` |
| **Audit logs** (consent changes, record access logs) | 5 years | Compliance best practice | `AuditLogRetention = 365 * 5 days` |
| **Session analytics** (focus scores, session timing, learning analytics) | 2 years | Data minimization | `AnalyticsRetention = 365 * 2 days` |
| **Engagement data** (daily challenges, badges) | 1 year after inactivity | Data minimization | `EngagementRetention = 365 days` |
| **AI tutor conversations** (tutor messages, thread documents) | 90 days | Minimized PII exposure for third-party processor data | `TutorMessageRetention = 90 days` |
| **Misconception session events** | 30 days (hard cap: 90 days) | ADR-0003; COPPA data minimization | Per ADR-0003 retention table |
| **Anonymized aggregates** (k-anonymity, k >= 10) | Indefinite | No personal data | ADR-0003 |

Tenants (schools/institutes) may configure shorter retention periods via
`TenantRetentionPolicy` overrides. Tenants may **not** extend retention beyond
the defaults listed above.

### Right to erasure (Art 17)

Upon a valid erasure request, data enters a 30-day cooling period and is then
permanently deleted. The `RetentionWorker` accelerates erasure requests that
have passed their cooling period. Erasure covers: profile data, event streams,
tutor conversations, analytics events, and consent records.

---

## 6. Children's Rights and Enhanced Protections

### 6.1 Applicable laws

| Law | Jurisdiction | Key requirements for Cena |
|-----|-------------|---------------------------|
| **Israel PPL Amendment 13** | Israel | Enhanced protections for minors' personal data; Privacy Protection Officer required |
| **COPPA** (16 CFR Part 312) | United States | Verifiable parental consent for under-13s; data minimization; parental access and deletion rights |
| **GDPR Article 8** | EU / EEA | Parental consent required for under-16s (or lower threshold set by member states) in relation to information society services |
| **ICO Children's Code** | United Kingdom | 15 standards for age-appropriate design; profiling OFF by default; best interests assessment |

### 6.2 How we protect children

1. **Age-gated registration**: Date of birth is collected at registration to determine the consent tier ("child", "teen", "adult"). Age-gate design follows the ICO Age Appropriate Design Code.

2. **High-privacy defaults for minors**: 7 of 10 processing purposes default to OFF for minors. Only essential account operations and adaptive learning default to ON.

3. **Parental consent**: For minors, parental/guardian consent is required before processing non-essential data. The parent's email (`ParentEmail`, PII level: High) is collected for the consent flow.

4. **No behavioral profiling by default**: `PeerComparison`, `LeaderboardDisplay`, `SocialFeatures`, `BehavioralAnalytics`, and `ThirdPartyAi` are all OFF by default for minors.

5. **No marketing to children**: `MarketingNudges` defaults to OFF for all users and is consent-gated.

6. **Misconception data is session-scoped**: Per ADR-0003, specific error patterns identified during learning are never stored on a child's permanent profile. They are discarded after the session (30-day maximum retention).

7. **Dark-pattern ban**: Cena prohibits streak counters, loss-aversion copy, variable-ratio rewards, and other engagement mechanics that exploit children. This is enforced by CI scanner (`scripts/shipgate/scan.mjs`). See `docs/engineering/shipgate.md`.

8. **No ML training on student data**: Student data is never used to train machine learning models. See [ML Training Prohibition](ml-training-prohibition.md).

> **LEGAL REVIEW REQUIRED**: The children's version of this privacy notice must
> use age-appropriate language per ICO Children's Code Standard 4 (Transparency).
> A separate children's notice should be prepared and reviewed by child language
> specialists. The notice must be available in Hebrew, Arabic, and English.
> Verify that Israel PPL Amendment 13 requirements are fully satisfied by the
> controls described above.

---

## 7. Your Rights

Under GDPR, COPPA, FERPA, and the Israel Privacy Protection Law, you (or your
parent/guardian if you are a minor) have the following rights:

| Right | Legal basis | How to exercise | Implementation |
|-------|-------------|-----------------|----------------|
| **Access** | GDPR Art 15 / Israel PPL s.11(b) | Request a copy of all data we hold about you | Via `/api/me/gdpr/export` endpoint |
| **Rectification** | GDPR Art 16 | Correct inaccurate personal data | Via profile settings or support request |
| **Erasure** ("right to be forgotten") | GDPR Art 17 / COPPA 312.6 | Request deletion of your personal data | Via `/api/me/gdpr/erasure` endpoint; 30-day cooling period, then permanent deletion |
| **Data portability** | GDPR Art 20 | Receive your data in a structured, machine-readable format (JSON) | Via `StudentDataExporter.ExportAsync()` at `/api/me/gdpr/export`; includes profile, tutoring sessions, learning sessions, and all domain events |
| **Object to processing** | GDPR Art 21 | Object to processing based on legitimate interest | Via consent management UI or `/api/me/consent` endpoint |
| **Withdraw consent** | GDPR Art 7(3) | Withdraw any previously granted consent | Via consent management UI; `GdprConsentManager.RevokeConsentAsync()` with full audit trail |
| **Restrict processing** | GDPR Art 18 | Request limitation of processing | Via support request |
| **Parental access** (minors) | COPPA 312.6 / FERPA 99.10 | Parents may review, correct, or delete their child's data | Via parent dashboard (planned: FIND-privacy-001) |

### 7.1 Data portability (Art 20) implementation

The `StudentDataExporter` (`src/shared/Cena.Infrastructure/Compliance/StudentDataExporter.cs`)
produces a structured JSON document containing:

- Profile snapshot with PII level annotations per field
- Tutoring session history (session ID, subject, topic, timestamps, turn count, status)
- Learning session history (session ID, subjects, mode, timestamps, questions attempted/correct)
- All domain events in the student's event stream
- Export metadata (timestamp, schema version, portability notice)

The export schema is versioned (current: v2.0) to ensure forward compatibility.

### 7.2 How to contact us

To exercise any of these rights, contact:

- **DPO / PPO**: **PENDING APPOINTMENT** (FIND-privacy-014)
- **Email**: [privacy@cena.edu]
- **Response time**: Within 30 days of receipt (extendable by 60 days for complex requests per GDPR Art 12(3))

> **LEGAL REVIEW REQUIRED**: Confirm the contact email address and response
> time commitments. Confirm whether a postal address is required. Confirm
> supervisory authority complaint rights language for each jurisdiction
> (Israel PPA, relevant EU SA, FTC).

---

## 8. Automated Decision-Making and Profiling

Cena uses automated profiling to deliver adaptive learning:

- **Elo rating**: A numerical skill rating used to calibrate question difficulty to the learner's ability level
- **Bayesian Knowledge Tracing (BKT)**: A probabilistic model estimating mastery of individual skills
- **IRT ability estimation**: Item Response Theory parameters for standardized assessments

These profiles directly affect which questions are presented and at what
difficulty. You have the right to:

- Request human review of automated decisions (GDPR Art 22(3))
- Object to profiling (GDPR Art 21)
- Have profiling turned off entirely (for minors, most profiling defaults to OFF)

Teachers can override automated profiling decisions via the admin dashboard.

---

## 9. Changes to This Notice

We will update this privacy notice when our data practices change. Material
changes will be communicated via:

- In-app notification at next login
- Email to registered email address (or parent email for minors)
- Updated "last modified" date on this document

---

## 10. Implementation Status

| Requirement | Code reference | Status |
|-------------|---------------|--------|
| PII classification system | `PiiClassification.cs`, `PiiScanner.cs` | Implemented |
| 10 granular processing purposes | `ProcessingPurpose.cs` | Implemented |
| Consent management with audit trail | `GdprConsentManager.cs` | Implemented |
| Minor default enforcement | `ProcessingPurposeExtensions.GetDefaultConsent()` | Implemented |
| Data portability export (Art 20) | `StudentDataExporter.cs` | Implemented (v2.0) |
| Retention enforcement | `RetentionWorker.cs`, `DataRetentionPolicy.cs` | Implemented |
| PII log sanitization | `PiiLogSanitizer.cs` | Implemented |
| Age gate and parental consent flow | -- | PLANNED (FIND-privacy-001) |
| Complete right-to-erasure | `RightToErasureService.cs` | Incomplete (FIND-privacy-005) |
| PII scrubbing for Anthropic | -- | PLANNED (FIND-privacy-008) |
| DPO / PPO appointment | -- | PENDING (FIND-privacy-014) |
| Privacy notice translation (HE, AR, EN) | -- | Not started |
| Children's version of this notice | -- | Not started |

---

## Review History

| Date | Reviewer | Notes |
|------|----------|-------|
| 2026-04-13 | claude-code | Skeleton created (GD-005) |
| 2026-04-13 | claude-code | Substantive rewrite with codebase-derived content |
