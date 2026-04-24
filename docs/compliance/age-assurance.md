# Age Assurance -- Cena Adaptive Learning Platform

**Status: DRAFT -- awaiting DPO appointment and legal counsel review**
**Reference**: AA-CENA-2026-Q2
**Version**: 1.0
**Date**: 2026-04-13
**Author**: Cena Engineering (automated compliance tooling)
**DPO sign-off**: PENDING (FIND-privacy-014)
**Legal basis**: GDPR Art 8, COPPA 16 CFR 312.3, Israel PPL Amendment 13, ICO Children's Code Std 7/11/12
**Next review date**: 2026-07-13 (quarterly) or upon material change

---

> This document describes the age assurance mechanisms implemented in the Cena
> platform for determining the age tier of users and applying appropriate consent
> and processing defaults. It is a DRAFT for legal review and must not be
> treated as final legal advice.

---

## Table of Contents

1. [Purpose](#1-purpose)
2. [Regulatory Requirements](#2-regulatory-requirements)
3. [Age Data Model](#3-age-data-model)
4. [Age Determination Flow](#4-age-determination-flow)
5. [Consent Tier Branching](#5-consent-tier-branching)
6. [Fail-Safe Behaviour](#6-fail-safe-behaviour)
7. [Age Verification Method](#7-age-verification-method)
8. [Implementation Status](#8-implementation-status)
9. [Code References](#9-code-references)
10. [Review History](#10-review-history)

---

## 1. Purpose

Cena serves students aged 6--18, the majority of whom are minors. Different
privacy regulations impose different obligations depending on the user's age:

- Under 13: COPPA requires verifiable parental consent before any data
  collection.
- Under 16: GDPR Art 8 requires parental consent for information society
  services (ISS). Israel follows the GDPR age threshold of 16.
- 16 and over: May consent independently under GDPR; still subject to enhanced
  protections under ICO Children's Code for under-18s.

The age assurance system must reliably determine a user's age tier at
registration and apply the correct consent requirements and processing defaults
before any personal data is processed.

---

## 2. Regulatory Requirements

### 2.1 GDPR Article 8

Where consent is the lawful basis for processing, the processing of personal
data of a child below the age of 16 is lawful only if consent is given or
authorised by the holder of parental responsibility. Member states may lower the
threshold to 13. Israel has not lowered the threshold, so 16 applies.

### 2.2 COPPA (16 CFR Part 312)

For US students under 13, COPPA requires:
- Direct notice to parents (312.4)
- Verifiable parental consent before collecting, using, or disclosing personal
  information from children (312.5)
- Reasonable methods for verification (312.5(b))

### 2.3 Israel PPL Amendment 13

Amendment 13 (2024) to the Protection of Privacy Law introduces enhanced
protections for minors' personal data, including:
- Mandatory Privacy Protection Officer (PPO) for entities processing minors'
  data at scale
- Enhanced consent requirements for minors

> **LEGAL REVIEW REQUIRED**: Confirm the specific requirements of Israel PPL
> Amendment 13 as they apply to educational technology platforms. In particular:
> (a) what age threshold defines a "minor" under Amendment 13, (b) whether
> there are specific age verification requirements beyond self-declaration,
> and (c) which Israeli Education Ministry requirements apply to edtech
> platforms collecting student data.

### 2.4 ICO Children's Code (Age Appropriate Design Code)

- **Standard 7 (Default settings)**: Settings must be "high privacy" by default
  for children.
- **Standard 11 (Parental controls)**: Age-appropriate parental controls must
  be provided.
- **Standard 12 (Profiling)**: Profiling of children must be switched off by
  default unless there is a compelling reason to enable it.

### 2.5 ICO v. Reddit (Feb 2026) precedent

The ICO's enforcement action against Reddit established that age assurance
must be more than self-declaration for services likely to be accessed by
children. This directly impacts Cena's age verification method.

---

## 3. Age Data Model

### 3.1 StudentProfileSnapshot fields

The `StudentProfileSnapshot` (Marten inline projection) stores age-related
data with PII annotations:

```
[Pii(PiiLevel.High, "identity")]
public DateOnly? DateOfBirth { get; set; }

public int? AgeAtRegistration { get; set; }

public string ConsentTier { get; set; } = "unknown";
    // Values: "adult" | "teen" | "child" | "unknown"

[Pii(PiiLevel.High, "contact")]
public string? ParentEmail { get; set; }

public bool ParentalConsentGiven { get; set; }

public string ConsentStatus { get; set; } = "unknown_needs_reverification";
    // Values: "verified" | "pending_parent" | "not_required"
    //       | "unknown_needs_reverification"
```

**Source**: `src/actors/Cena.Actors/Events/StudentProfileSnapshot.cs` lines 51--58

### 3.2 AgeAndConsentRecorded_V1 event

Age data is captured at registration via the `AgeAndConsentRecorded_V1` event:

```
public record AgeAndConsentRecorded_V1(
    string StudentId,
    DateOnly DateOfBirth,
    int AgeAtRegistration,
    string ConsentTier,               // "adult" | "teen" | "child"
    string? ParentEmail,              // required when ConsentTier != "adult"
    bool ParentalConsentGiven,        // true if parent completed verification
    string? ParentalConsentToken,     // opaque token for parent verification
    string ConsentStatus              // "verified" | "pending_parent" | "not_required"
);
```

**Source**: `src/actors/Cena.Actors/Events/LearnerEvents.cs` lines 374--382

### 3.3 Consent tier definitions

| Tier | Age Range | Regulatory Trigger | Parental Consent Required |
|---|---|---|---|
| `"child"` | Under 13 | COPPA | Yes -- verifiable parental consent |
| `"teen"` | 13--15 | GDPR Art 8 (Israel threshold: 16) | Yes -- parental authorisation |
| `"adult"` | 16+ | None (but ICO Children's Code applies to under-18) | No |
| `"unknown"` | Not determined | Fail-safe: treated as minor | Yes -- system defaults to minor protections |

---

## 4. Age Determination Flow

### 4.1 Registration sequence

1. User creates an account via Firebase Auth (email + password).
2. The registration flow collects date of birth.
3. `AgeAtRegistration` is computed from `DateOfBirth` at the time of the event.
4. `ConsentTier` is derived from `AgeAtRegistration`:
   - Under 13 -> `"child"`
   - 13--15 -> `"teen"`
   - 16+ -> `"adult"`
5. The `AgeAndConsentRecorded_V1` event is appended to the student's event
   stream.
6. The `StudentProfileSnapshot` projection applies the event, setting all
   age/consent fields.
7. If `ConsentTier` is `"child"` or `"teen"`, the registration flow branches to
   the parental consent collection sub-flow (see `parental-consent.md`).

### 4.2 Existing student handling

Students who registered before the age gate was implemented have
`ConsentTier = "unknown"` and `ConsentStatus = "unknown_needs_reverification"`.
These students are treated as minors by the fail-safe mechanism (Section 6)
until they complete age re-verification.

---

## 5. Consent Tier Branching

The `ConsentTier` drives which processing purposes are enabled by default:

| Processing Purpose | `"adult"` | `"teen"` | `"child"` | `"unknown"` |
|---|---|---|---|---|
| `AccountAuth` | ON | ON | ON | ON |
| `SessionContinuity` | ON | ON | ON | ON |
| `AdaptiveRecommendation` | ON | ON (with parental consent) | ON (with parental consent) | ON (fail-safe: blocked until consent) |
| `PeerComparison` | ON | **OFF** | **OFF** | **OFF** |
| `LeaderboardDisplay` | ON | **OFF** | **OFF** | **OFF** |
| `SocialFeatures` | ON | **OFF** | **OFF** | **OFF** |
| `ThirdPartyAi` | ON | **OFF** | **OFF** | **OFF** |
| `BehavioralAnalytics` | ON | **OFF** | **OFF** | **OFF** |
| `CrossTenantBenchmarking` | OFF | **OFF** | **OFF** | **OFF** |
| `MarketingNudges` | OFF | **OFF** | **OFF** | **OFF** |

The defaults are computed by `ProcessingPurpose.GetDefaultConsent(bool isMinor)`
using the `[MinorDefault]` attribute on each enum member. For minors, 7 of 10
purposes default to OFF. Only the two contract-necessary purposes and
`AdaptiveRecommendation` default to ON.

---

## 6. Fail-Safe Behaviour

### 6.1 Default-to-minor principle

Both the `ConsentEnforcementMiddleware` and the `RequiresConsentAttribute`
default to `isMinor=true` when the user's age is unknown:

```csharp
// ConsentEnforcementMiddleware.cs line 110:
hasConsent = await consentManager.HasConsentAsync(
    studentId, purpose, isMinor: true, context.RequestAborted);

// RequiresConsentAttribute.cs line 60:
var hasConsent = await consentManager.HasConsentAsync(
    studentId, _purpose, isMinor: true, httpContext.RequestAborted);
```

This means:

- If a user has no `AgeAndConsentRecorded_V1` event in their stream, they are
  treated as a minor.
- All minor-default-OFF purposes are blocked until either (a) age is verified
  and the user is confirmed as an adult, or (b) parental consent is obtained.
- This is a fail-safe mechanism: the system errs on the side of maximum
  protection when age is uncertain.

### 6.2 ConsentTier "unknown" handling

The default value of `ConsentTier` in `StudentProfileSnapshot` is `"unknown"`.
The default value of `ConsentStatus` is `"unknown_needs_reverification"`. Both
values signal that the student has not completed the age gate and must be
treated as a minor until verified.

---

## 7. Age Verification Method

> **LEGAL REVIEW REQUIRED**: The specific age verification method has not been
> finalised. The following options are under consideration and require legal
> counsel input before implementation:
>
> **Option A: Self-declaration (date of birth entry)**
> - Current implementation: user provides DOB during registration.
> - Risk: The ICO v. Reddit (2026) precedent established that self-declaration
>   alone is insufficient for services likely to be accessed by children.
> - Mitigation: Could be combined with teacher attestation in classroom mode.
>
> **Option B: Teacher/school attestation**
> - In classroom mode, the teacher or school administrator attests to the
>   student's age tier during class setup.
> - Advantage: Leverages the school's existing records; aligns with FERPA
>   "school official" designation.
> - Risk: Does not cover consumer (non-classroom) mode.
>
> **Option C: Document verification**
> - Upload of ID document or school enrollment record.
> - Advantage: Highest assurance level.
> - Risk: Introduces additional PII processing (identity documents);
>   disproportionate for an educational platform; creates additional data
>   protection obligations.
>
> **Recommended approach** (subject to legal review): A combined method where
> classroom users are verified by teacher attestation (Option B) and consumer
> users use self-declaration plus parental consent verification (Option A +
> parental email challenge from `parental-consent.md`).

> **LEGAL REVIEW REQUIRED**: Confirm which Israeli Education Ministry
> requirements apply to age verification in educational technology platforms,
> and whether Ministry-issued student identifiers can be used for age
> verification in lieu of date-of-birth collection.

---

## 8. Implementation Status

| Requirement | Code Reference | Status |
|---|---|---|
| DateOfBirth collection at registration | `StudentProfileSnapshot.DateOfBirth` | Implemented (field exists; UI flow pending) |
| ConsentTier computation from age | `AgeAndConsentRecorded_V1` event | Implemented |
| Minor default protections (7/10 OFF) | `ProcessingPurpose` `[MinorDefault]` attributes | Implemented |
| Fail-safe default to isMinor=true | `ConsentEnforcementMiddleware`, `RequiresConsentAttribute` | Implemented |
| Age re-verification for legacy users | `ConsentStatus = "unknown_needs_reverification"` | Planned (re-verification flow not built) |
| Teacher attestation flow (classroom mode) | -- | Planned |
| Parental consent sub-flow for minors | See `parental-consent.md` | Partially implemented (see FIND-privacy-001) |
| Privacy notice at DOB collection point | -- | Planned |
| Age verification beyond self-declaration | -- | Pending legal review (Section 7) |

---

## 9. Code References

| Artifact | File | Description |
|---|---|---|
| Student profile snapshot | `src/actors/Cena.Actors/Events/StudentProfileSnapshot.cs` | PII-annotated fields: DateOfBirth, AgeAtRegistration, ConsentTier, ParentEmail, ParentalConsentGiven, ConsentStatus |
| Age/consent event | `src/actors/Cena.Actors/Events/LearnerEvents.cs` | `AgeAndConsentRecorded_V1` record capturing DOB at registration |
| Processing purposes | `src/shared/Cena.Infrastructure/Compliance/ProcessingPurpose.cs` | 10 purposes with `[MinorDefault]` attribute driving consent defaults |
| Consent enforcement middleware | `src/shared/Cena.Infrastructure/Compliance/ConsentEnforcementMiddleware.cs` | Defaults to `isMinor=true` when age unknown (line 110) |
| Consent attribute | `src/shared/Cena.Infrastructure/Compliance/RequiresConsentAttribute.cs` | Defaults to `isMinor=true` when age unknown (line 60) |
| Consent manager | `src/shared/Cena.Infrastructure/Compliance/GdprConsentManager.cs` | `GetDefaultConsent(isMinor)` returns age-appropriate defaults |

---

## 10. Review History

| Date | Version | Reviewer | Changes |
|---|---|---|---|
| 2026-04-13 | 1.0 | claude-code (GD-005) | Initial age assurance document -- DRAFT; derived from codebase analysis of StudentProfileSnapshot, AgeAndConsentRecorded_V1 event, consent tier system, and fail-safe middleware |
