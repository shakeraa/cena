# Parental Consent Flow -- Cena Adaptive Learning Platform

**Status: DRAFT -- awaiting DPO appointment and legal counsel review**
**Reference**: PC-CENA-2026-Q2
**Version**: 1.0
**Date**: 2026-04-13
**Author**: Cena Engineering (automated compliance tooling)
**DPO sign-off**: PENDING (FIND-privacy-014)
**Legal basis**: GDPR Art 8, COPPA 16 CFR 312.5, Israel PPL Amendment 13, ICO Children's Code Std 11
**Next review date**: 2026-07-13 (quarterly) or upon material change

---

> This document describes the parental consent collection, storage, enforcement,
> and revocation mechanisms implemented in the Cena platform. It is a DRAFT for
> legal review and must not be treated as final legal advice.

---

## Table of Contents

1. [Purpose](#1-purpose)
2. [Regulatory Requirements](#2-regulatory-requirements)
3. [Consent Data Model](#3-consent-data-model)
4. [Consent Collection Flow](#4-consent-collection-flow)
5. [Consent Defaults for Minors](#5-consent-defaults-for-minors)
6. [Consent Enforcement](#6-consent-enforcement)
7. [Consent Revocation](#7-consent-revocation)
8. [Consent Audit Trail](#8-consent-audit-trail)
9. [Consent Mechanism](#9-consent-mechanism)
10. [Implementation Status](#10-implementation-status)
11. [Code References](#11-code-references)
12. [Review History](#12-review-history)

---

## 1. Purpose

Cena's primary user base is students aged 6--18, the majority of whom are
minors. For minors, most processing activities require parental consent before
data can be collected or processed. This document defines how parental consent
is obtained, recorded, enforced, and revoked.

---

## 2. Regulatory Requirements

### 2.1 GDPR Article 8

For information society services offered directly to a child below the age of
16 (in Israel), processing is lawful only if consent is given or authorised by
the holder of parental responsibility. The controller must make reasonable
efforts to verify that consent is given or authorised by the holder of parental
responsibility, taking into consideration available technology.

### 2.2 COPPA (16 CFR 312.5)

For US children under 13, COPPA requires verifiable parental consent (VPC)
before collecting personal information. Acceptable VPC methods include:

- Signed consent form returned by mail, fax, or electronic scan
- Credit card or other online payment system used for a monetary transaction
- Video conference call with trained personnel
- Government-issued ID verified against a database, with the ID deleted promptly
- Knowledge-based challenge questions
- **Email plus** method: email combined with additional verification step

### 2.3 Israel PPL Amendment 13

Enhanced consent requirements for processing minors' data. Specific
requirements are under legal review.

### 2.4 ICO Children's Code Standard 11

Provide age-appropriate parental controls. Give parents the ability to monitor
their child's activity and exercise rights on their child's behalf.

---

## 3. Consent Data Model

### 3.1 Parent contact information

Stored on `StudentProfileSnapshot`:

```
[Pii(PiiLevel.High, "contact")]
public string? ParentEmail { get; set; }

public bool ParentalConsentGiven { get; set; }

public string ConsentStatus { get; set; } = "unknown_needs_reverification";
    // Values: "verified" | "pending_parent" | "not_required"
    //       | "unknown_needs_reverification"
```

**Source**: `src/actors/Cena.Actors/Events/StudentProfileSnapshot.cs` lines 55--58

### 3.2 Per-purpose consent records

The `GdprConsentManager` stores granular per-purpose consent in `ConsentRecord`
documents:

```csharp
public sealed class ConsentRecord
{
    public Guid Id { get; set; }
    public string StudentId { get; set; } = "";
    public ProcessingPurpose Purpose { get; set; }
    public bool Granted { get; set; }
    public DateTimeOffset GrantedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}
```

**Source**: `src/shared/Cena.Infrastructure/Compliance/GdprConsentManager.cs` lines 30--38

Each `ConsentRecord` tracks:
- **Which student** the consent applies to (`StudentId`)
- **Which processing purpose** is being consented to (`Purpose` -- one of 10
  `ProcessingPurpose` enum values)
- **Whether consent is currently active** (`Granted`)
- **When consent was granted** (`GrantedAt`)
- **When consent was revoked**, if applicable (`RevokedAt`)

### 3.3 Consent change audit log

Every consent change is recorded in `ConsentChangeLog` for compliance audit:

```csharp
public sealed class ConsentChangeLog
{
    public Guid Id { get; set; }
    public string StudentId { get; set; } = "";
    public ProcessingPurpose Purpose { get; set; }
    public bool? PreviousValue { get; set; }
    public bool NewValue { get; set; }
    public DateTimeOffset ChangedAt { get; set; }
    public string ChangedBy { get; set; } = "";   // e.g. "parent:xyz@email.com"
    public string Source { get; set; } = "";       // "ui" | "api" | "system"
}
```

**Source**: `src/shared/Cena.Infrastructure/Compliance/GdprConsentManager.cs` lines 15--25

---

## 4. Consent Collection Flow

### 4.1 Registration-time consent for minors

1. User registers and completes the age gate (see `age-assurance.md`).
2. If `ConsentTier` is `"child"` (under 13) or `"teen"` (13--15):
   a. Registration flow collects `ParentEmail`.
   b. The `AgeAndConsentRecorded_V1` event is appended with
      `ParentalConsentGiven = false` and `ConsentStatus = "pending_parent"`.
   c. A consent verification request is sent to the parent's email address
      containing a `ParentalConsentToken` (opaque token).
   d. The student can access the platform in a restricted mode (only
      contract-necessary purposes: AccountAuth, SessionContinuity) until
      parental consent is verified.
3. When the parent completes the verification challenge:
   a. `ParentalConsentGiven` is set to `true`.
   b. `ConsentStatus` is set to `"verified"`.
   c. The parent's consent preferences for each non-contract purpose are
      recorded via `GdprConsentManager.RecordConsentChangeAsync()`.
   d. Processing purposes the parent opted into are activated.

### 4.2 Consent for existing students without age verification

Students with `ConsentStatus = "unknown_needs_reverification"` must complete
the age gate and (if minor) the parental consent flow before non-essential
processing can be activated. The fail-safe mechanism (Section 6 below) blocks
these purposes until then.

---

## 5. Consent Defaults for Minors

When a minor registers, the system applies high-privacy defaults per
`ProcessingPurpose.GetDefaultConsent(isMinor: true)`. The parent can then opt
in to additional purposes during the consent flow.

| Processing Purpose | Minor Default | Parent Can Opt In? |
|---|---|---|
| `AccountAuth` | **ON** (always required) | No -- cannot be disabled |
| `SessionContinuity` | **ON** (always required) | No -- cannot be disabled |
| `AdaptiveRecommendation` | **ON** | Yes (opt out available) |
| `PeerComparison` | **OFF** | Yes |
| `LeaderboardDisplay` | **OFF** | Yes |
| `SocialFeatures` | **OFF** | Yes |
| `ThirdPartyAi` | **OFF** | Yes |
| `BehavioralAnalytics` | **OFF** | Yes |
| `CrossTenantBenchmarking` | **OFF** | Yes |
| `MarketingNudges` | **OFF** | Yes |

**Summary**: 7 of 10 purposes are OFF by default for minors. The parent must
affirmatively opt in for each purpose they wish to enable. Purposes not
opted into remain OFF.

---

## 6. Consent Enforcement

### 6.1 ConsentEnforcementMiddleware

The `ConsentEnforcementMiddleware` is registered in the ASP.NET Core request
pipeline after authentication and authorization. For every request to an
endpoint marked with `[RequiresConsent(ProcessingPurpose)]`:

1. Extracts the student ID from claims, route values, or query string.
2. Checks consent via `IGdprConsentManager.HasConsentAsync()`.
3. Uses request-scoped caching to avoid repeated DB calls within the same
   request.
4. **Defaults to `isMinor=true`** when the student's age is unknown -- this is
   the fail-safe mechanism that ensures maximum protection.
5. If consent is missing, returns **HTTP 403 Forbidden** with:
   - Header: `X-Consent-Required: true`
   - Body: `{"error": "consent_required", "purpose": "<purpose_name>"}`

### 6.2 RequiresConsentAttribute

The `[RequiresConsent]` attribute implements `IEndpointFilter` for ASP.NET Core
Minimal APIs. It performs the same consent validation as the middleware but at
the endpoint filter level, providing defence-in-depth.

### 6.3 Admin override

Administrators can bypass consent enforcement via the `?skipConsentCheck=true`
query parameter, but only if they hold an admin role (verified by the
middleware). All admin bypasses are logged as `[SIEM] ConsentCheckSkipped`
events.

---

## 7. Consent Revocation

### 7.1 Revocation mechanism

Parents can revoke consent for any non-contract purpose at any time:

1. Parent accesses the consent management interface.
2. Parent toggles a purpose to OFF.
3. `GdprConsentManager.RevokeConsentAsync()` is called:
   - The `ConsentRecord.Granted` field is set to `false`.
   - `ConsentRecord.RevokedAt` is set to `DateTimeOffset.UtcNow`.
4. `RecordConsentChangeAsync()` creates a `ConsentChangeLog` entry with:
   - `PreviousValue = true`, `NewValue = false`
   - `ChangedBy` = parent identifier
   - `Source` = `"ui"` or `"api"`
5. Subsequent requests to endpoints requiring that purpose are blocked by
   `ConsentEnforcementMiddleware`.

### 7.2 Effect of revocation

When consent is revoked for a purpose:
- The purpose is immediately blocked for new requests.
- No retroactive deletion of data processed under the previous consent is
  performed automatically. A separate erasure request (via
  `RightToErasureService`) must be submitted if the parent wants data
  processed under the revoked consent to be deleted.

> **LEGAL REVIEW REQUIRED**: Confirm the required timing for consent revocation
> to take effect. GDPR Art 7(3) states that withdrawal of consent shall not
> affect the lawfulness of processing based on consent before its withdrawal.
> However, confirm whether any specific timing requirements apply under Israeli
> law or COPPA for ceasing processing after parental consent revocation (COPPA
> 312.6(a)(2) requires operators to "promptly" respond to parent revocation).

---

## 8. Consent Audit Trail

All consent changes are recorded in the `ConsentChangeLog` document store,
providing a complete audit trail for compliance purposes:

| Field | Description |
|---|---|
| `Id` | Unique identifier for the change event |
| `StudentId` | The student whose consent was changed |
| `Purpose` | The processing purpose affected |
| `PreviousValue` | The consent state before the change (null for first recording) |
| `NewValue` | The consent state after the change |
| `ChangedAt` | UTC timestamp of the change |
| `ChangedBy` | Identity of who made the change (parent email, admin ID, or "system") |
| `Source` | Channel through which the change was made ("ui", "api", "system") |

The audit trail is retained for the account lifetime plus 3 years as proof of
lawful basis (see `data-retention.md`).

---

## 9. Consent Mechanism

> **LEGAL REVIEW REQUIRED**: The specific consent verification mechanism has not
> been finalised. The following design decisions require legal counsel input:
>
> **Consent collection method**:
> - Current design: Email verification (parent receives email with consent
>   token, clicks link to confirm). This satisfies the COPPA "email plus"
>   method if combined with a secondary step (e.g., delayed confirmation,
>   follow-up email after 48 hours).
> - Alternative: Signed consent form (PDF) uploaded or returned via mail/fax.
> - Question: Is email verification sufficient for COPPA VPC and GDPR Art 8
>   "reasonable efforts" in an educational context where teachers can also
>   attest to parental involvement?

> **LEGAL REVIEW REQUIRED**: Confirm the required duration for storing consent
> records. Current policy is account lifetime + 3 years. GDPR requires
> controllers to be able to demonstrate that consent was validly obtained
> (Art 7(1)). Confirm whether 3 years post-account-deletion is sufficient,
> or whether a longer period is needed given limitation periods for regulatory
> enforcement actions.

> **LEGAL REVIEW REQUIRED**: The parental consent form and privacy notice must
> be provided in the languages understood by the parent population. For Cena's
> target markets, this means at minimum:
> - **Hebrew** (Israel primary market)
> - **Arabic** (Israeli Arab population, potential Gulf markets)
> - **English** (international users)
>
> Confirm whether additional languages are required by Israeli law or the
> specific school districts being served.

---

## 10. Implementation Status

| Requirement | Code Reference | Status |
|---|---|---|
| ParentEmail collection at registration | `StudentProfileSnapshot.ParentEmail` | Implemented (field exists; UI flow pending) |
| ParentalConsentGiven tracking | `StudentProfileSnapshot.ParentalConsentGiven` | Implemented |
| ConsentStatus state machine | `StudentProfileSnapshot.ConsentStatus` | Implemented |
| Per-purpose consent records | `GdprConsentManager.ConsentRecord` | Implemented |
| Consent change audit log | `GdprConsentManager.ConsentChangeLog` | Implemented |
| Consent revocation | `GdprConsentManager.RevokeConsentAsync()` | Implemented |
| Consent enforcement middleware | `ConsentEnforcementMiddleware` | Implemented |
| Consent endpoint filter | `RequiresConsentAttribute` | Implemented |
| Fail-safe default to isMinor=true | Middleware + attribute | Implemented |
| Parental consent email verification flow | -- | Planned (FIND-privacy-001) |
| Parent consent management dashboard | -- | Planned |
| Consent form translations (Hebrew, Arabic, English) | -- | Planned |
| COPPA "email plus" secondary verification step | -- | Planned |

---

## 11. Code References

| Artifact | File | Description |
|---|---|---|
| Student profile snapshot | `src/actors/Cena.Actors/Events/StudentProfileSnapshot.cs` | ParentEmail, ParentalConsentGiven, ConsentStatus fields |
| Age/consent event | `src/actors/Cena.Actors/Events/LearnerEvents.cs` | `AgeAndConsentRecorded_V1` with ParentEmail and ParentalConsentToken |
| Consent manager | `src/shared/Cena.Infrastructure/Compliance/GdprConsentManager.cs` | ConsentRecord, ConsentChangeLog, RecordConsentAsync, RevokeConsentAsync, RecordConsentChangeAsync |
| Consent enforcement middleware | `src/shared/Cena.Infrastructure/Compliance/ConsentEnforcementMiddleware.cs` | Pipeline middleware blocking endpoints without consent |
| Consent attribute | `src/shared/Cena.Infrastructure/Compliance/RequiresConsentAttribute.cs` | Endpoint filter for Minimal APIs |
| Processing purposes | `src/shared/Cena.Infrastructure/Compliance/ProcessingPurpose.cs` | 10 purposes with `[MinorDefault]` attributes |
| Consent endpoints | `src/api/Cena.Student.Api.Host/Endpoints/ConsentEndpoints.cs` | Student-facing consent management API |
| GDPR endpoints (student) | `src/api/Cena.Student.Api.Host/Endpoints/MeGdprEndpoints.cs` | Student GDPR rights endpoints |
| GDPR endpoints (admin) | `src/api/Cena.Admin.Api/GdprEndpoints.cs` | Admin GDPR management endpoints |

---

## 12. Review History

| Date | Version | Reviewer | Changes |
|---|---|---|---|
| 2026-04-13 | 1.0 | claude-code (GD-005) | Initial parental consent document -- DRAFT; derived from codebase analysis of consent manager, consent records, enforcement middleware, and age/consent event model |
