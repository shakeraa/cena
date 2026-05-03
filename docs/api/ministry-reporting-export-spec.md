# Ministry reporting export — endpoint spec (Launch = spec-only)

**Source task**: [prr-235](../../tasks/pre-release-review/TASK-PRR-235-ministry-reporting-export-spec.md)
**Personas raising**: persona-ministry, persona-enterprise
**Epic**: [EPIC-PRR-F](../../tasks/pre-release-review/EPIC-PRR-F-multi-target-onboarding-plan.md)
**Status at Launch**: **SPEC ONLY** — no runtime endpoint ships with Launch.
Full implementation is scheduled for Q3 (post-launch tenancy Phase 3 rollout).
This doc fixes the contract so the multi-target data model
([ADR-0050](../adr/0050-multi-target-student-plan.md)) does not paint us
into a corner when the Q3 implementation lands.

---

## 1. Motivation

Tenants that report to the Ministry of Education or to a school district
need structured export of student enrollment, exam-target selection, and
accommodation usage. persona-ministry surfaced two requirements:

1. **Enrollment by שאלון.** For each student in a classroom, which
   Ministry question-paper codes are they preparing for this academic
   year.
2. **Accommodation usage aggregate.** Count of students exercising PRR-080
   accommodations (extra time, reader, calculator), without PII.

persona-enterprise additionally asked for:

3. **School report shape.** A superset of (1) that includes classroom +
   tenant-assigned targets only (Student-self-assigned is excluded
   unless the student consents).

This spec defines the shape now so ADR-0050's `ExamTarget` value object
already carries the fields needed. Implementing the endpoint later is a
straightforward projection over existing event-sourced state.

## 2. Non-goals (at Launch and at Q3)

- **No magen (מגן) field.** No predicted-score field. persona-ministry was
  explicit: the Ministry wants enrollment + accommodation data, not
  performance prediction. See [memory: Honest not complimentary].
- **No free-text reason fields.** ADR-0050 §1 banned free-text at the VO;
  the export honours that — only the enum `reason_tag` is exported.
- **No raw Ministry-paper content.** The export references Ministry paper
  codes only; student answers to Ministry-copyright items are never
  exported (see [memory: Bagrut reference-only]).

## 3. Endpoint shape

```
GET /api/export/exam-targets
  ?enrollmentId={enrollmentId}
  &format=csv|json|ministry-xml
  &asOf={ISO-8601-date}   # optional, default "now"
  &include={all|active}   # optional, default "active"
```

### 3a. Authorization

- JWT-auth with tenant-scoped role `InstituteAdmin` or
  `MinistryLiaison`.
- Tenant-scoping enforced at the controller seam; the projection query
  filters on tenant-id derived from JWT, never from URL path.
- Per [ADR-0001](../adr/0001-multi-institute-enrollment.md), cross-tenant
  reads are impossible by construction (queries cannot hold two tenant
  IDs at once).

### 3b. Audit logging

Every call emits an audit event:

```jsonc
{
  "event": "MinistryExportRequested_V1",
  "tenant_id": "inst-123",
  "requested_by": "user-abc",
  "enrollment_id": "enr-xyz",
  "format": "ministry-xml",
  "row_count": 42,
  "as_of": "2026-06-30T00:00:00Z",
  "include": "active",
  "ts": "2026-06-30T12:05:00Z"
}
```

Event is stored in `CENA_AUDIT_EVENTS` aggregate, 7-year retention per
regulatory minimum. No row-level student data in the audit — only counts.

### 3c. Pagination + rate-limit

- Default page size 500 rows.
- Next-page token returned in response metadata.
- Rate-limited at 10 requests / minute / tenant-admin. Ministry exports
  are bulk operations; high-rate pollers indicate automation that should
  use the planned webhook (out of scope for this spec).

## 4. Exported fields

Per the prr-235 task body. Numbered to allow downstream CSV column
ordering to be stable.

| # | Field | Type | Source | Included in school report? | Notes |
|---|-------|------|--------|----------------------------|-------|
| 1 | `student_id` | opaque string | StudentAggregate | Yes | Tenant-local stable id. Never a national id. |
| 2 | `enrollment_id` | opaque string | EnrollmentAggregate | Yes | ADR-0001 identifier. |
| 3 | `exam_code` | string | ExamTarget.ExamCode | Yes | Catalog PK (e.g. `BAGRUT_MATH_5U`). |
| 4 | `ministry_subject_code` | string\|null | catalog lookup | Yes | e.g. `"035"`. Null for non-Ministry catalogs. |
| 5 | `ministry_question_paper_codes[]` | string[] | ExamTarget.QuestionPaperCodes | Yes | שאלון list. Non-empty for Bagrut; empty for SAT/PET. |
| 6 | `track` | string\|null | ExamTarget.Track | Yes | e.g. `"5U"`, `"ModuleA"`, or null. |
| 7 | `sitting_code` | string | ExamTarget.Sitting | Yes | `"תשפ\"ו/Summer/A"` shape. |
| 8 | `academic_year` | string | ExamTarget.Sitting.AcademicYear | Yes | Hebrew or Gregorian. |
| 9 | `season` | string | ExamTarget.Sitting.Season | Yes | `Summer`\|`Winter`. |
| 10 | `moed` | string | ExamTarget.Sitting.Moed | Yes | `A`\|`B`\|`C`\|`Special`. |
| 11 | `source` | string | ExamTarget.Source | Filter rule (§5) | `Student`\|`Classroom`\|`Tenant`\|`Migration`. |
| 12 | `assigned_by_id` | opaque string | ExamTarget.AssignedById | Yes | Student/teacher/admin user id. |
| 13 | `weekly_hours` | int | ExamTarget.WeeklyHours | Yes | 1..40. |
| 14 | `reason_tag` | string\|null | ExamTarget.ReasonTag | Yes | Enum-only; free-text banned (ADR-0050 §1). |
| 15 | `created_at` | ISO-8601 | ExamTargetAdded_V1.At | Yes | When the target was first added. |
| 16 | `archived_at` | ISO-8601\|null | ExamTargetArchived_V1.At | Yes | Null for active targets. Terminal per ADR-0050 §6. |
| 17 | `export_readiness` | enum | catalog flag | Yes | `exportable`\|`requires-consent`. See §5. |

## 5. Source-filter rule + export-readiness flag

### 5a. Default filter (`format=ministry-xml` or `format=csv&include=active`)

- `source ∈ {Classroom, Tenant}` → always exportable (school has lawful
  basis under ADR-0001; teacher or admin assigned).
- `source ∈ {Student, Migration}` → excluded unless the student has
  opted in to sharing self-assigned targets with the school.

### 5b. `export_readiness` flag

`ExamTarget` VO carries an export-readiness marker derived from `source`
and the student's sharing consent:

```csharp
public enum ExamTargetExportReadiness
{
    /// <summary>Classroom- or Tenant-assigned → school has lawful basis.</summary>
    Exportable = 0,

    /// <summary>Student-self-assigned → requires student consent before
    /// including in a school-scope export.</summary>
    RequiresConsent = 1,
}

public static ExamTargetExportReadiness ClassifyExportReadiness(
    ExamTargetSource source,
    bool studentHasSchoolSharingConsent)
{
    if (source is ExamTargetSource.Classroom or ExamTargetSource.Tenant)
        return ExamTargetExportReadiness.Exportable;
    return studentHasSchoolSharingConsent
        ? ExamTargetExportReadiness.Exportable
        : ExamTargetExportReadiness.RequiresConsent;
}
```

The classifier lives alongside the VO when Q3 implementation lands. It is
intentionally not implemented at Launch — Launch-era code never emits
the field because no exporter exists.

### 5c. Migration cohort

`ExamTargetSource.Migration` rows (ADR-0050 §prr-219 upcast from the
legacy single-target StudentPlanConfig) are treated as
`RequiresConsent` because the original consent UI predates school-sharing
tracking. A one-time sweep (scheduled for Q3-pre-export) will ask
migration-cohort students to confirm school-sharing if they want their
historical targets included.

## 6. Formats

### 6a. JSON (primary)

Shape stored at
[`contracts/ministry-exports/schema.json`](../../contracts/ministry-exports/schema.json).
Validated by the Q3 endpoint before writing the response body.

### 6b. CSV

Column headers = field names from §4 in declared order. UTF-8 encoding
with BOM so Excel on Windows opens it correctly. Cells containing comma,
newline, or quote are double-quoted per RFC 4180.

Schema draft at
[`contracts/ministry-exports/csv-shape.md`](../../contracts/ministry-exports/csv-shape.md).

### 6c. Ministry XML (Q3 deferred)

The Ministry-defined XML shape is pending clarification from the
persona-ministry liaison. The JSON + CSV formats above are authoritative
at Launch; the XML format will be added as a separate task in Q3 once
the Ministry publishes the XSD. Out of scope for this spec beyond the
note that the same `export_readiness` filter applies.

## 7. Data model annotations (at Launch)

Per prr-235 DoD, the `ExamTarget` VO should be annotated to indicate
which fields are export-ready at Q3. We do **not** add the runtime
classifier (§5b) at Launch — only a doc-comment so future readers know
the plan.

Proposed annotation (to land as a follow-up PR after prr-235 spec review,
before the Q3 implementation sprint):

```csharp
// On ExamTarget record:
// <remarks>
// Export-readiness (prr-235): Source=Classroom|Tenant → Exportable;
// Source=Student|Migration → RequiresConsent. See
// docs/api/ministry-reporting-export-spec.md §5 for the Q3 endpoint
// that projects this value.
// </remarks>
```

That edit is intentionally separate from this spec so the spec review
(persona-ministry + persona-enterprise) can land without touching the
aggregate VO.

## 8. Non-negotiables honoured

- **ADR-0001 tenancy isolation** — every query filters on tenant-id from
  JWT.
- **Bagrut reference-only** — Ministry paper codes are exported, not
  Ministry item content.
- **No stubs in production** — no stub endpoint ships at Launch. Per
  task body §DoD: "No implementation code — spec only." A stub endpoint
  returning 501 was considered and rejected as a violation of the
  no-stubs memory (2026-04-11).
- **Misconception session-scope (ADR-0003)** — export does not include
  misconception data; misconception records are session-scoped and not a
  student attribute.
- **Dark-pattern mechanics banned** — export is read-only; the endpoint
  generates no student-facing UX.

## 9. Q3 implementation checklist (out-of-scope at Launch)

When implementation picks up:

- [ ] Add `ExamTargetExportReadiness` enum + classifier alongside
      `ExamTarget`.
- [ ] Add `StudentSharingConsentGranted` event to the Student aggregate
      (prr-235-impl); backfill via migration.
- [ ] Wire the endpoint: `MinistryReportsExportController` (tenant-admin
      scope), `ExamTargetExportProjection` (Marten read-model).
- [ ] Add audit event `MinistryExportRequested_V1`.
- [ ] Add CSV + JSON schema validators
      (`contracts/ministry-exports/schema.json` + `csv-shape.md`).
- [ ] Coordinate with persona-ministry on XML XSD (§6c).
- [ ] Extend synthetic probe (prr-039 pattern) to cover the endpoint
      smoke path.
- [ ] Extend prr-074 rate-limit middleware with the 10/min cap.

## 10. Related

- [prr-235](../../tasks/pre-release-review/TASK-PRR-235-ministry-reporting-export-spec.md) — this task
- [prr-037](../../tasks/pre-release-review/TASK-PRR-037-grade-passback.md) — grade passback (related integration)
- [prr-072](../../tasks/pre-release-review/TASK-PRR-072-coverage-matrix.md) — coverage matrix
- [prr-080](../../tasks/pre-release-review/TASK-PRR-080-accommodations-respect.md) — accommodations (aggregate feed for (2) above)
- [ADR-0001](../adr/0001-multi-institute-enrollment.md) — tenancy isolation
- [ADR-0050](../adr/0050-multi-target-student-plan.md) — ExamTarget VO
- [EPIC-PRR-F](../../tasks/pre-release-review/EPIC-PRR-F-multi-target-onboarding-plan.md) — multi-target onboarding epic
- [`contracts/ministry-exports/schema.json`](../../contracts/ministry-exports/schema.json) — JSON schema
- [`contracts/ministry-exports/csv-shape.md`](../../contracts/ministry-exports/csv-shape.md) — CSV shape
