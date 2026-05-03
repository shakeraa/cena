# Classroom vs Consumer Mode Split -- Cena Adaptive Learning Platform

**Status: DRAFT -- awaiting legal counsel review**
**Created**: 2026-04-13 (GD-005)
**Legal basis**: FERPA 34 CFR 99.31(a)(1) (school official exception), COPPA 16 CFR 312.5 (parental consent), GDPR Art 26 (joint controllers), Israel PPL Amendment 13
**Version**: 1.0

---

## 1. Purpose

Cena serves two fundamentally different contexts with different legal
obligations: **school-directed** classrooms where a teacher or institution
controls the learning environment, and **consumer/parent-directed** contexts
where a parent signs up a child directly on the platform. This document maps
Cena's codebase constructs to the legal distinctions between these contexts
and identifies the consent, data governance, and controller-relationship
implications of each.

---

## 2. Institute Type Classification

The `InstituteType` enum (`src/shared/Cena.Infrastructure/Documents/InstituteDocument.cs`)
defines five types of educational organizations that can operate on Cena:

| InstituteType | Description | Typical context | Controller relationship |
|---------------|-------------|-----------------|------------------------|
| `Platform` | The Cena platform itself (seeded as "cena-platform") | Consumer/parent-directed | Cena is sole controller |
| `School` | Formal educational institution (K-12, secondary) | School-directed | School is controller; Cena is processor (or joint controller -- see Section 6) |
| `PrivateTutor` | Independent tutor operating their own practice | Tutor-directed | Tutor is controller for their students; Cena is joint controller for infrastructure |
| `CramSchool` | Supplementary education center (e.g. after-school program) | Institution-directed | Similar to School; institution is controller |
| `NGO` | Non-governmental organization providing educational services | Varies | Depends on NGO's relationship with students |

Code reference: `InstituteDocument.cs` lines 11-18.

---

## 3. Classroom Mode Classification

The `ClassroomMode` enum (`src/shared/Cena.Infrastructure/Documents/ClassroomDocument.cs`)
defines how students interact with a classroom:

| ClassroomMode | Value | Description | Typical institute type | Consent model |
|---------------|-------|-------------|----------------------|---------------|
| `InstructorLed` | 0 | Teacher-led with scheduled sessions (default for school classrooms) | School, CramSchool | School may delegate consent under FERPA school-official exception |
| `SelfPaced` | 1 | Students progress at their own pace through the curriculum | Platform (consumer) | Full parental consent required (no school delegation) |
| `PersonalMentorship` | 2 | 1:1 mentoring (Phase 2) | PrivateTutor | Tutor-specific consent; parent must consent to tutor relationship |

Code reference: `ClassroomDocument.cs` lines 13-23.

---

## 4. Join Approval Mode

The `JoinApprovalMode` enum controls how students enter a classroom:

| JoinApprovalMode | Value | Description | Privacy implication |
|------------------|-------|-------------|---------------------|
| `AutoApprove` | 0 | Anyone with the join code is admitted immediately | Lower barrier; relies on code secrecy for access control |
| `ManualApprove` | 1 | Join requests require teacher/mentor approval | Teacher gatekeeping; suitable for school classrooms |
| `InviteOnly` | 2 | Students can only join via explicit invite link (no public join code) | Highest control; required for PrivateTutor and sensitive contexts |

Code reference: `ClassroomDocument.cs` lines 28-38.

---

## 5. Three Operational Contexts

### 5.1 School-directed (InstructorLed + School/CramSchool institute)

**How it works**:

- A school creates an institute on Cena (type: `School`)
- Teachers create `InstructorLed` classrooms within the institute
- The school provides the student list; students join via join code or invite
- The teacher controls the classroom: curriculum, pacing, visibility settings
- `JoinApprovalMode` is typically `ManualApprove` or `InviteOnly`

**Consent model**:

Under FERPA 34 CFR 99.31(a)(1), a school may designate Cena as a "school
official" with a "legitimate educational interest," potentially allowing the
school to provide student data to Cena without individual parental consent for
the educational records aspects. The school's existing FERPA annual
notification would cover this disclosure.

However:

- FERPA's school-official exception covers **educational records** only
- Processing purposes beyond educational necessity (e.g. `BehavioralAnalytics`,
  `ThirdPartyAi`, `LeaderboardDisplay`) still require separate consent
- For US students under 13, COPPA's verifiable parental consent requirements
  apply independently of FERPA
- The school must execute a FERPA agreement designating Cena as a school
  official (see `docs/compliance/ferpa-agreement.md`)

**Data governance**:

- All student data is scoped to the school's institute via `ClassroomDocument.InstituteId`
- Teachers see only students within their institute (enforced by `TenantScope.GetInstituteFilter()`)
- No cross-institute data leakage (see Section 7)

> **LEGAL REVIEW REQUIRED**: Determine whether the FERPA "school official"
> exception applies to Israeli schools (FERPA is a US federal law). For
> Israeli schools, consent must be analyzed under the Israel PPL and the
> school's own data protection obligations. Prepare a jurisdiction-specific
> consent matrix mapping each InstituteType + country combination to the
> applicable consent framework.

### 5.2 Consumer/parent-directed (SelfPaced + Platform institute)

**How it works**:

- A parent signs up their child directly on the Cena platform
- The student is enrolled in the default "cena-platform" institute (type: `Platform`)
- The student uses `SelfPaced` classrooms or self-study mode
- There is no school or teacher intermediary
- The parent is the sole consenting adult

**Consent model**:

- **Full parental consent is required** before any data processing for minors
- There is no school-official delegation -- Cena cannot rely on FERPA exceptions
- COPPA verifiable parental consent (16 CFR 312.5) applies for US children under 13
- GDPR Art 8 parental consent applies for EU/EEA children under 16
- Israel PPL Amendment 13 enhanced protections apply for Israeli children
- All 8 consent-gated processing purposes require explicit parental opt-in

**Data governance**:

- Student data is scoped to the "cena-platform" institute
- No teacher has access unless the parent explicitly enrolls the child in a
  teacher's classroom
- Cena is the sole data controller

### 5.3 Private tutor-directed (PersonalMentorship + PrivateTutor institute)

**How it works**:

- A private tutor creates an institute (type: `PrivateTutor`)
- The tutor creates `PersonalMentorship` classrooms for 1:1 or small-group tutoring
- Students join via `InviteOnly` (most private)
- The tutor controls the curriculum and pacing for their students
- `MentorIds` on the `ClassroomDocument` identifies the assigned tutor(s)

**Consent model**:

- The tutor-student relationship is typically arranged by the parent
- The parent must consent to both: (a) the tutor's use of Cena, and (b) Cena's
  data processing
- The tutor cannot delegate consent on behalf of the parent
- All consent-gated purposes require explicit parental opt-in

**Data governance**:

- Student data is scoped to the tutor's institute
- The tutor sees only their own students
- Cena is a **joint controller** with the tutor: the tutor determines the
  purposes of processing (which students, what curriculum), while Cena
  determines the means (platform infrastructure, data storage, AI tutoring)

> **LEGAL REVIEW REQUIRED**: Determine the precise controller relationship
> between Cena and private tutors. Under GDPR Art 26, joint controllers must
> have a transparent arrangement determining their respective responsibilities
> for compliance. Prepare a joint controller agreement template for private
> tutors. Determine whether private tutors in Israel need to register as
> "database owners" under the Israel PPL.

---

## 6. Controller vs Processor Relationship with Schools

The controller/processor distinction has significant legal consequences:

| Model | Cena's role | School's role | Implication |
|-------|-------------|---------------|-------------|
| **Processor** | Processes data on school's instructions | Controller -- determines purposes and means | Cena needs a DPA (GDPR Art 28); school handles DSARs; Cena acts only on documented instructions |
| **Joint controller** | Co-determines purposes (adaptive learning, AI tutoring) | Co-determines purposes (curriculum, enrollment) | Both are responsible; need Art 26 arrangement; both handle DSARs |

**Cena's position**: Cena likely acts as a **joint controller** with schools
because Cena independently determines several processing purposes (adaptive
recommendation algorithm, AI tutoring, behavioral analytics, platform
analytics) that go beyond merely executing the school's instructions.

The school determines: which students enroll, what curriculum is used, which
teachers have access, when to archive a classroom.

Cena determines: how adaptive learning works, what data the AI tutor receives,
how profiling algorithms operate, what retention periods apply, what engagement
mechanics are used (or banned).

> **LEGAL REVIEW REQUIRED**: Determine definitively whether Cena is a
> "processor" or "joint controller" with schools under GDPR Art 26. This
> distinction affects: (a) who handles DSARs, (b) whether a DPA or joint
> controller arrangement is needed, (c) liability allocation for data
> breaches, (d) who must appoint a DPO. Prepare a specific consent delegation
> agreement template for schools that covers both FERPA (for US schools) and
> GDPR/Israel PPL (for Israeli and EU schools).

---

## 7. Data Segregation and Tenant Isolation

### 7.1 Institute-level scoping

Every classroom belongs to exactly one institute via the `InstituteId` foreign
key on `ClassroomDocument`:

```csharp
// ClassroomDocument.cs
/// <summary>Foreign key to InstituteDocument. Null for legacy/pre-tenancy classrooms.</summary>
public string? InstituteId { get; set; }
```

All data queries for teachers and administrators pass through
`TenantScope.GetInstituteFilter()` (`src/shared/Cena.Infrastructure/Tenancy/TenantScope.cs`),
which extracts the user's `institute_id` claim and restricts query results:

```csharp
// TenantScope.cs -- returns the institute IDs this user is authorized to access
public static IReadOnlyList<string> GetInstituteFilter(
    ClaimsPrincipal user,
    string? defaultInstituteId = "cena-platform")
```

- `SUPER_ADMIN` returns empty list (unrestricted -- sees all tenants)
- All other roles return a single-element list with their institute ID
- Students without an institute claim default to "cena-platform"

### 7.2 No cross-institute data leakage

The tenant isolation model ensures:

- A School A teacher **cannot** see School B students
- A private tutor **cannot** see another tutor's students
- Platform (consumer) students **cannot** see school classroom data
- `CrossTenantBenchmarking` uses only anonymized aggregates and requires
  explicit consent (default OFF for all users)

### 7.3 School-level scoping (legacy)

For backward compatibility, `TenantScope.GetSchoolFilter()` provides
school-level filtering via the `school_id` claim. This is the Phase 1
mechanism; Phase 2 will expand to multi-institute membership.

---

## 8. Consent Matrix by Context

| Processing purpose | School-directed (InstructorLed) | Consumer (SelfPaced) | Tutor (PersonalMentorship) |
|--------------------|--------------------------------|---------------------|---------------------------|
| AccountAuth | Required (contract) | Required (contract) | Required (contract) |
| SessionContinuity | Required (contract) | Required (contract) | Required (contract) |
| AdaptiveRecommendation | School may delegate (FERPA) | Parental consent required | Parental consent required |
| PeerComparison | School may delegate; minor default OFF | Parental consent; minor default OFF | Parental consent; minor default OFF |
| LeaderboardDisplay | School may delegate; minor default OFF | Parental consent; minor default OFF | Not applicable (1:1 context) |
| SocialFeatures | School may delegate; minor default OFF | Parental consent; minor default OFF | Not applicable (1:1 context) |
| ThirdPartyAi | Requires explicit consent even in school context | Parental consent; minor default OFF | Parental consent; minor default OFF |
| BehavioralAnalytics | School may delegate; minor default OFF | Parental consent; minor default OFF | Parental consent; minor default OFF |
| CrossTenantBenchmarking | Requires explicit consent; default OFF for all | Requires explicit consent; default OFF for all | Requires explicit consent; default OFF for all |
| MarketingNudges | Requires explicit consent; default OFF for all | Requires explicit consent; default OFF for all | Requires explicit consent; default OFF for all |

> **LEGAL REVIEW REQUIRED**: Validate each cell in this matrix against
> applicable law for each jurisdiction (Israel, US/FERPA, EU/GDPR, UK). In
> particular, confirm that "school may delegate" is legally valid in each
> jurisdiction and for each purpose. ThirdPartyAi (Anthropic) may require
> separate consent even in the FERPA school-official context because it
> involves a cross-border transfer to a US-based third party.

---

## 9. Implementation Status

| Requirement | Code reference | Status |
|-------------|---------------|--------|
| InstituteType enum (5 types) | `InstituteDocument.cs` | Implemented |
| ClassroomMode enum (3 modes) | `ClassroomDocument.cs` | Implemented |
| JoinApprovalMode enum (3 modes) | `ClassroomDocument.cs` | Implemented |
| InstituteId FK on ClassroomDocument | `ClassroomDocument.cs` | Implemented |
| TenantScope.GetInstituteFilter() | `TenantScope.cs` | Implemented |
| TenantScope.GetSchoolFilter() (legacy) | `TenantScope.cs` | Implemented |
| Processing purposes with minor defaults | `ProcessingPurpose.cs` | Implemented |
| Consent management per purpose | `GdprConsentManager.cs` | Implemented |
| FERPA school-official agreement template | `ferpa-agreement.md` | Skeleton |
| Joint controller agreement (tutors) | -- | Not started |
| Consent delegation agreement (schools) | -- | Not started |
| Jurisdiction-specific consent matrix | -- | Not started |
| Multi-institute membership (Phase 2) | -- | Planned (TENANCY-P2) |

---

## Review History

| Date | Reviewer | Notes |
|------|----------|-------|
| 2026-04-13 | claude-code | Skeleton created (GD-005) |
| 2026-04-13 | claude-code | Substantive rewrite with codebase-derived content |
