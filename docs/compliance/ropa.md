# Record of Processing Activities (ROPA) -- Cena Adaptive Learning Platform

**Status: DRAFT -- awaiting DPO appointment and legal counsel review**
**ROPA Reference**: ROPA-CENA-2026-Q2
**Version**: 1.0
**Date**: 2026-04-13
**Author**: Cena Engineering (automated compliance tooling)
**DPO sign-off**: PENDING (FIND-privacy-014)
**Legal basis**: GDPR Article 30, Israel PPL Amendment 13, FERPA
**Next review date**: 2026-07-13 (quarterly) or upon material change to processing

---

> This ROPA is prepared in accordance with GDPR Article 30, which requires
> controllers to maintain a record of processing activities under their
> responsibility. It follows the Article 29 Working Party (WP29) template
> structure. This document is a DRAFT for legal review and must not be treated
> as final legal advice.

---

## Table of Contents

1. [Controller Details](#1-controller-details)
2. [Data Protection Officer](#2-data-protection-officer)
3. [Processing Activities Register](#3-processing-activities-register)
4. [Consent Defaults by Age Tier](#4-consent-defaults-by-age-tier)
5. [International Data Transfers](#5-international-data-transfers)
6. [Technical and Organisational Measures](#6-technical-and-organisational-measures)
7. [Code References](#7-code-references)
8. [Review History](#8-review-history)

---

## 1. Controller Details

| Field | Value |
|---|---|
| **Controller name** | Cena Platform |
| **Country of establishment** | Israel |
| **Contact email** | PENDING |
| **Website** | PENDING |
| **Representative in EEA** (Art 27) | PENDING -- required if processing data of EEA residents without EEA establishment |

> **LEGAL REVIEW REQUIRED**: Confirm the legal entity name, registered address,
> and whether an Art 27 EEA representative is required based on the platform's
> user base geography. If the controller is an Israeli company processing data
> of EU/EEA residents, an EEA representative must be designated.

---

## 2. Data Protection Officer

| Field | Value |
|---|---|
| **DPO appointed** | PENDING (tracked as FIND-privacy-014) |
| **DPO name** | -- |
| **DPO contact** | -- |

Israel PPL Amendment 13 requires appointment of a Privacy Protection Officer
(PPO) for entities processing personal data of minors at scale. GDPR Article
37(1) requires a DPO where core activities consist of processing on a large
scale of special categories of data or data relating to children.

> **LEGAL REVIEW REQUIRED**: Confirm whether the Israeli PPO requirement and
> the GDPR DPO requirement can be satisfied by the same individual, or whether
> separate appointments are needed for each jurisdiction.

---

## 3. Processing Activities Register

The codebase defines 10 granular processing purposes in
`ProcessingPurpose.cs`. Each purpose carries a lawful basis via the
`[LawfulBasis]` attribute and age-appropriate defaults via `[MinorDefault]`.

| # | Processing Activity | Data Categories | Data Subjects | Legal Basis (GDPR Art 6) | Retention Period | Processor / Sub-processor | Purpose Code |
|---|---|---|---|---|---|---|---|
| 1 | **Account authentication** | Email, display name, hashed password | Students, teachers, admins | Art 6(1)(b) Contract | Account lifetime + 30 days post-deletion (`ErasureWorkerOptions.CoolingPeriod`) | Firebase Auth (Google Cloud, multi-region) | `AccountAuth` |
| 2 | **Session management** | Session tokens, device metadata, anonymized IP | Students | Art 6(1)(b) Contract | Session duration; IP anonymized within 90 days | In-house (PostgreSQL / Marten) | `SessionContinuity` |
| 3 | **Adaptive learning recommendations** | Elo rating, BKT mastery state (p(Known)), response history, time-on-task, methodology assignment | Students (including minors) | Art 6(1)(a) Consent (parental for minors) | Account lifetime; 7 years after last enrollment per `StudentRecordRetention` | In-house (PostgreSQL / Marten event store) | `AdaptiveRecommendation` |
| 4 | **Peer comparison** | Anonymized aggregate performance vs. peer groups | Students | Art 6(1)(a) Consent | Account lifetime | In-house | `PeerComparison` |
| 5 | **Leaderboard display** | Display name, XP points, streak count | Students | Art 6(1)(a) Consent | Account lifetime; engagement data purged 1 year after inactivity per `EngagementRetention` | In-house | `LeaderboardDisplay` |
| 6 | **Social features** | Posts, comments, display name, visibility setting | Students | Art 6(1)(a) Consent | Account lifetime | In-house | `SocialFeatures` |
| 7 | **AI tutoring (Anthropic Claude)** | Free-text conversation messages, student context (subject, mastery level, Elo rating) | Students (including minors) | Art 6(1)(a) Consent (parental for minors) | 90 days (`TutorMessageRetention`); enforced by `RetentionWorker` | Anthropic (San Francisco, CA, USA) | `ThirdPartyAi` |
| 8 | **Behavioral analytics** | Focus tracking events, engagement signals, session timing, time-on-task | Students | Art 6(1)(a) Consent | 2 years (`AnalyticsRetention`); enforced by `RetentionWorker` | In-house (PostgreSQL / Marten) | `BehavioralAnalytics` |
| 9 | **Cross-tenant benchmarking** | Anonymized aggregate metrics across schools/institutes | Students (aggregated) | Art 6(1)(a) Consent | 2 years (`AnalyticsRetention`) | In-house | `CrossTenantBenchmarking` |
| 10 | **Administrative analytics** | Per-student and aggregate academic performance, mastery tracking, focus scores | Students, teachers | Art 6(1)(f) Legitimate interest | Account lifetime | In-house | -- (not consent-gated; legitimate interest) |

### 3.1 Notes on lawful basis

- **Activities 1--2** (AccountAuth, SessionContinuity): Processing is necessary
  for contract performance. These purposes are always required and cannot be
  disabled by the user (`ProcessingPurpose.IsAlwaysRequired()` returns `true`).

- **Activities 3--9**: Processing is based on consent under Art 6(1)(a). For
  minors under 16, parental consent is required per GDPR Art 8. Consent records
  are managed by `GdprConsentManager` and stored as `ConsentRecord` documents
  in PostgreSQL/Marten.

- **Activity 10**: Processing is based on legitimate interest under Art 6(1)(f).
  Schools have a legitimate interest in monitoring aggregate and individual
  student performance to fulfil their educational mandate. A Legitimate Interest
  Assessment (LIA) should be documented separately.

> **LEGAL REVIEW REQUIRED**: Confirm that Art 6(1)(f) legitimate interest is
> the appropriate basis for administrative analytics, particularly where it
> involves per-student (non-aggregated) performance data for minors. Consider
> whether Art 6(1)(e) public task may be more appropriate for
> state-funded educational institutions in Israel.

---

## 4. Consent Defaults by Age Tier

The `ProcessingPurpose.GetDefaultConsent(bool isMinor)` method returns
age-appropriate defaults. When age is unknown, the system defaults to
`isMinor=true` (fail-safe, per `ConsentEnforcementMiddleware` line 110 and
`RequiresConsentAttribute` line 60).

| Processing Purpose | Adult Default | Minor Default (under 16) |
|---|---|---|
| `AccountAuth` | ON (always required) | ON (always required) |
| `SessionContinuity` | ON (always required) | ON (always required) |
| `AdaptiveRecommendation` | ON | **ON** |
| `PeerComparison` | ON | **OFF** |
| `LeaderboardDisplay` | ON | **OFF** |
| `SocialFeatures` | ON | **OFF** |
| `ThirdPartyAi` | ON | **OFF** |
| `BehavioralAnalytics` | ON | **OFF** |
| `CrossTenantBenchmarking` | OFF | **OFF** |
| `MarketingNudges` | OFF | **OFF** |

**Summary**: For minors, 7 of 10 purposes default to OFF. Only AccountAuth,
SessionContinuity, and AdaptiveRecommendation default to ON. The first two are
contract-necessary and cannot be toggled. AdaptiveRecommendation is the core
educational purpose and requires parental consent before activation for minors.

---

## 5. International Data Transfers

### 5.1 Transfer destinations

| Processor | Country | Service | Transfer Mechanism | DPA Status |
|---|---|---|---|---|
| **Anthropic** | United States | AI tutoring (Claude API) | Standard Contractual Clauses (SCCs) required | **PENDING** -- DPA not yet executed |
| **Google / Firebase** | Multi-region (Google Cloud) | Authentication (Firebase Auth) | SCCs / Adequacy decision (varies by region) | **PENDING** -- Google Cloud DPA terms to be reviewed |

### 5.2 Supplementary measures for US transfers

For transfers to Anthropic (US-based):

- **Encryption in transit**: All API calls to `api.anthropic.com` use TLS 1.2+.
- **PII scrubbing**: Planned (FIND-privacy-008) -- strip or pseudonymize
  personally identifiable information from conversation context before
  transmission to Anthropic.
- **Retention minimization**: Tutor conversations retained for 90 days only
  (`TutorMessageRetention`), then purged by `RetentionWorker`.
- **Contractual prohibition on ML training**: Anthropic must not use student
  conversation data for model training (see `ml-training-prohibition.md`).

> **LEGAL REVIEW REQUIRED**: Confirm that SCCs (June 2021 version) plus the
> supplementary measures listed above constitute adequate transfer safeguards
> for children's educational data to a US processor, in light of the Schrems II
> ruling and any applicable Israeli cross-border transfer requirements under
> the PPL.

---

## 6. Technical and Organisational Measures

### 6.1 Technical measures (GDPR Art 32)

| Measure | Implementation | Code Reference |
|---|---|---|
| **Encryption at rest** | PostgreSQL data-at-rest encryption (provider-managed keys) | Infrastructure configuration |
| **Encryption in transit** | TLS 1.2+ for all API endpoints and database connections | Infrastructure configuration |
| **PII classification** | `[Pii(PiiLevel, category)]` attribute on all PII fields in `StudentProfileSnapshot` | `StudentProfileSnapshot.cs` |
| **Consent enforcement** | `ConsentEnforcementMiddleware` + `[RequiresConsent]` attribute block endpoints without valid consent | `ConsentEnforcementMiddleware.cs`, `RequiresConsentAttribute.cs` |
| **Automated data retention** | `RetentionWorker` runs daily at 2 AM, purges expired documents per category | `RetentionWorker.cs` |
| **Automated erasure** | `ErasureWorker` runs daily at 3 AM, processes requests past 30-day cooling period | `ErasureWorker.cs` |
| **Right to erasure** | `RightToErasureService` deletes personal data across all stores | `RightToErasureService.cs` |
| **Audit logging** | `ConsentChangeLog` records all consent changes with ChangedBy, Source, PreviousValue, NewValue | `GdprConsentManager.cs` |
| **SIEM integration** | All consent checks, enforcement blocks, and retention runs emit structured `[SIEM]` log events | Throughout compliance layer |
| **Tenant isolation** | Row-level security and tenant-scoped queries prevent cross-tenant data access | Marten configuration |

### 6.2 Organisational measures

| Measure | Status |
|---|---|
| DPO / PPO appointment | PENDING (FIND-privacy-014) |
| Data Processing Agreements with all processors | PENDING |
| Staff data protection training | PENDING |
| Incident response procedures | PENDING |
| Data Subject Access Request (DSAR) procedures | Partial (StudentDataExporter exists) |
| Regular DPIA reviews | Quarterly schedule established (see `dpia-2026-04.md`) |

---

## 7. Code References

| Artifact | File | Description |
|---|---|---|
| Processing purposes | `src/shared/Cena.Infrastructure/Compliance/ProcessingPurpose.cs` | 10 granular purposes with lawful basis and age-appropriate defaults |
| Consent manager | `src/shared/Cena.Infrastructure/Compliance/GdprConsentManager.cs` | ConsentRecord + ConsentChangeLog document store |
| Consent enforcement | `src/shared/Cena.Infrastructure/Compliance/ConsentEnforcementMiddleware.cs` | Middleware blocking endpoints without consent |
| Consent attribute | `src/shared/Cena.Infrastructure/Compliance/RequiresConsentAttribute.cs` | Endpoint filter for Minimal APIs |
| Data retention policy | `src/shared/Cena.Infrastructure/Compliance/DataRetentionPolicy.cs` | Retention period constants per category |
| Retention worker | `src/shared/Cena.Infrastructure/Compliance/RetentionWorker.cs` | Background service enforcing retention |
| Erasure worker | `src/shared/Cena.Infrastructure/Compliance/ErasureWorker.cs` | Background service processing erasure requests |
| Right to erasure | `src/shared/Cena.Infrastructure/Compliance/RightToErasureService.cs` | Erasure request lifecycle (CoolingPeriod -> Processing -> Completed) |
| Student profile | `src/actors/Cena.Actors/Events/StudentProfileSnapshot.cs` | PII-annotated snapshot with age gate fields |
| Compliance contracts | `src/shared/Cena.Infrastructure/Compliance/ComplianceContracts.cs` | DataCategory enum, TenantRetentionPolicy, RetentionRunHistory |

---

## 8. Review History

| Date | Version | Reviewer | Changes |
|---|---|---|---|
| 2026-04-13 | 1.0 | claude-code (GD-005) | Initial ROPA -- DRAFT; derived from codebase analysis of 10 processing purposes, retention policies, and consent infrastructure |
