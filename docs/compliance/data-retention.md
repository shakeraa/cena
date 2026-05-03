# Data Retention Policy -- Cena Adaptive Learning Platform

**Status: DRAFT -- awaiting DPO appointment and legal counsel review**
**Reference**: DRP-CENA-2026-Q2
**Version**: 1.0
**Date**: 2026-04-13
**Author**: Cena Engineering (automated compliance tooling)
**DPO sign-off**: PENDING (FIND-privacy-014)
**Legal basis**: GDPR Art 5(1)(e), COPPA 16 CFR 312.10, FERPA 34 CFR 99, Israel PPL
**Next review date**: 2026-07-13 (quarterly) or upon material change

---

> This document defines the data retention periods, automated enforcement
> mechanisms, and erasure procedures for all categories of personal data
> processed by the Cena platform. It is a DRAFT for legal review and must not
> be treated as final legal advice.

---

## Table of Contents

1. [Purpose](#1-purpose)
2. [Principles](#2-principles)
3. [Retention Schedule](#3-retention-schedule)
4. [Automated Retention Enforcement](#4-automated-retention-enforcement)
5. [Right to Erasure (Account Deletion)](#5-right-to-erasure-account-deletion)
6. [Tenant-Specific Overrides](#6-tenant-specific-overrides)
7. [Monitoring and Audit](#7-monitoring-and-audit)
8. [Implementation Status](#8-implementation-status)
9. [Code References](#9-code-references)
10. [Review History](#10-review-history)

---

## 1. Purpose

GDPR Article 5(1)(e) (storage limitation) requires that personal data be kept
in a form which permits identification of data subjects for no longer than is
necessary for the purposes for which the personal data are processed. COPPA
16 CFR 312.10 requires operators to retain personal information collected from
children only as long as is reasonably necessary to fulfil the purpose for
which the information was collected.

This policy defines specific retention periods for each category of data
processed by Cena, and describes the automated background services that
enforce them.

---

## 2. Principles

1. **Minimization**: Retain only what is necessary for the stated purpose.
2. **Defined periods**: Every data category has a concrete retention period,
   codified as constants in `DataRetentionPolicy.cs`.
3. **Automated enforcement**: Background workers (`RetentionWorker`,
   `ErasureWorker`) enforce retention without manual intervention.
4. **Tenant flexibility**: Tenants (schools/institutes) can request shorter
   retention periods via `TenantRetentionPolicy` overrides; longer periods
   are not permitted without legal justification.
5. **Audit trail**: Every retention run and erasure action is logged in
   `RetentionRunHistory` for compliance audit.

---

## 3. Retention Schedule

### 3.1 Retention periods by data category

| # | Data Category | Retention Period | Justification | Code Constant | Documents Affected |
|---|---|---|---|---|---|
| 1 | **Student education records** (event streams, mastery snapshots, tutoring transcripts) | 7 years after last enrollment | FERPA requirement for educational records | `StudentRecordRetention = TimeSpan.FromDays(365 * 7)` | Event streams, `StudentProfileSnapshot`, `StudentActivityDocument` |
| 2 | **Audit logs** (FERPA access logs, consent change logs) | 5 years | Compliance best practice; proof of lawful access and consent | `AuditLogRetention = TimeSpan.FromDays(365 * 5)` | `StudentRecordAccessLog` |
| 3 | **Session analytics** (focus scores, session timing, learning analytics) | 2 years | Sufficient for longitudinal educational insights | `AnalyticsRetention = TimeSpan.FromDays(365 * 2)` | `FocusSessionRollupDocument` |
| 4 | **Engagement data** (XP, streaks, badges, daily challenges) | 1 year after last activity | Non-essential gamification data; no educational necessity beyond 1 year | `EngagementRetention = TimeSpan.FromDays(365)` | `DailyChallengeCompletionDocument` |
| 5 | **AI tutor conversations** (Anthropic Claude) | 90 days | Third-party processor data minimization; shortest feasible window for context continuity | `TutorMessageRetention = TimeSpan.FromDays(90)` | `TutorMessageDocument`, `TutorThreadDocument` |
| 6 | **Consent records** (per-purpose consent grants/revocations) | Account lifetime + 3 years | Proof of lawful basis under GDPR Art 7(1); covers limitation period for regulatory actions | -- (not in `DataRetentionPolicy`; managed by consent manager) | `ConsentRecord`, `ConsentChangeLog` |
| 7 | **IP addresses** | Anonymized within 90 days | GDPR data minimization (Art 5(1)(c)); IP retained only for session management, then anonymized | -- (session-level policy) | Session metadata |
| 8 | **Account data post-deletion** | 30 days cooling period | Allows account recovery before permanent deletion; `ErasureWorkerOptions.CoolingPeriod` | `ErasureWorkerOptions.CoolingPeriod = TimeSpan.FromDays(30)` | All personal data for the student |

> **LEGAL REVIEW REQUIRED**: The 7-year student record retention period
> (`StudentRecordRetention`) is derived from FERPA, which is a US federal
> regulation. Confirm whether 7 years is appropriate under Israeli law. The
> Israel Protection of Privacy Law does not specify a fixed retention period
> for educational records; the principle is "no longer than necessary." For
> Israeli students not subject to FERPA, a shorter retention period (e.g.,
> 3--5 years) may be more appropriate. Consider implementing jurisdiction-based
> retention via `TenantRetentionPolicy` overrides.

> **LEGAL REVIEW REQUIRED**: The 90-day AI tutor conversation retention period
> (`TutorMessageRetention`) is a data minimization measure to limit PII
> exposure at the third-party processor (Anthropic). Confirm whether 90 days
> meets Israeli PPL requirements for data processed by foreign sub-processors.
> Also confirm whether the Anthropic DPA (when executed) imposes any minimum
> or maximum retention requirements on conversation data.

### 3.2 Data lifecycle states

```
Active Data ──> Retention Period Expires ──> Purged/Anonymized
                                                    |
                                                    v
                                           RetentionRunHistory
                                           (audit record kept)

Erasure Request ──> Cooling Period (30d) ──> Processing ──> Completed
       |                                                        |
       v                                                        v
  CoolingPeriod                                          All personal data
  (user can cancel)                                      permanently deleted
```

---

## 4. Automated Retention Enforcement

### 4.1 RetentionWorker

The `RetentionWorker` is a .NET `BackgroundService` (`IHostedService`) that
runs on a configurable cron schedule.

| Setting | Default | Configuration |
|---|---|---|
| **Schedule** | `0 2 * * *` (daily at 2:00 AM UTC) | `RetentionWorkerOptions.CronExpression` |
| **Soft delete** | Enabled (Marten soft-delete) | `RetentionWorkerOptions.UseSoftDelete` |
| **Batch size** | 1,000 documents per category | `RetentionWorkerOptions.BatchSize` |
| **Operation timeout** | 2 hours per category | `RetentionWorkerOptions.OperationTimeout` |
| **Batch delay** | 100ms between batches | `RetentionWorkerOptions.BatchDelay` |

**Processing sequence per run**:

1. **Student Education Records** (`DataCategory.StudentRecord`):
   Queries `StudentActivityDocument` for students with `LastActivityAt` older
   than `StudentRecordRetention` (7 years). Archives and soft-deletes inactive
   student records.

2. **Audit Logs** (`DataCategory.AuditLog`):
   Queries `StudentRecordAccessLog` for entries with `AccessedAt` older than
   `AuditLogRetention` (5 years). Hard-deletes expired audit logs.

3. **Session Analytics** (`DataCategory.Analytics`):
   Queries `FocusSessionRollupDocument` for entries with `Date` older than
   `AnalyticsRetention` (2 years). Deletes expired analytics documents.

4. **Engagement Data** (`DataCategory.Engagement`):
   Queries `DailyChallengeCompletionDocument` for entries with `CompletedAt`
   older than `EngagementRetention` (1 year). Deletes expired engagement data.

5. **Erasure Request Acceleration**:
   Queries `ErasureRequest` documents in `CoolingPeriod` status. If the
   cooling period (30 days) has elapsed, transitions the request to
   `Processing` status for the `ErasureWorker` to handle.

**Source**: `src/shared/Cena.Infrastructure/Compliance/RetentionWorker.cs`

### 4.2 ErasureWorker

The `ErasureWorker` is a .NET `BackgroundService` that processes GDPR
right-to-erasure requests after their cooling period.

| Setting | Default | Configuration |
|---|---|---|
| **Schedule** | `0 3 * * *` (daily at 3:00 AM UTC) | `ErasureWorkerOptions.CronExpression` |
| **Cooling period** | 30 days | `ErasureWorkerOptions.CoolingPeriod` |
| **Batch size** | 100 requests per run | `ErasureWorkerOptions.BatchSize` |
| **Operation timeout** | 1 hour | `ErasureWorkerOptions.OperationTimeout` |

**Erasure request lifecycle**:

```
Requested ──> CoolingPeriod ──> Processing ──> Completed
                   |                              |
                   v                              v
              Cancelled                    All personal data
              (by user)                    permanently deleted
```

1. **Requested**: Initial request received from student or parent.
2. **CoolingPeriod**: 30-day cooling period begins. User can cancel the request
   during this period (status transitions to `Cancelled`).
3. **Processing**: Cooling period has elapsed. The `ErasureWorker` invokes
   `IRightToErasureService.ProcessErasureAsync()` to delete all personal data
   across all stores.
4. **Completed**: All personal data for the student has been permanently
   deleted. Anonymized aggregate data may be retained.

**Source**: `src/shared/Cena.Infrastructure/Compliance/ErasureWorker.cs`

---

## 5. Right to Erasure (Account Deletion)

### 5.1 Erasure scope

When an erasure request is processed, the `RightToErasureService` deletes
personal data across the following stores:

| Data Store | What is Deleted |
|---|---|
| Marten event stream | Student's event stream (all events) |
| `StudentProfileSnapshot` | Full profile snapshot document |
| `ConsentRecord` | All consent records for the student |
| `ConsentChangeLog` | All consent change audit entries |
| `StudentRecordAccessLog` | All access log entries for the student |
| `TutorMessageDocument` | All AI tutor conversation messages |
| `TutorThreadDocument` | All AI tutor conversation threads |
| `FocusSessionRollupDocument` | All focus/analytics data |
| `DailyChallengeCompletionDocument` | All engagement data |
| `StudentActivityDocument` | Activity tracking document |
| Firebase Auth | Authentication account (delegated to Firebase Admin SDK) |

### 5.2 Retained after erasure

- **Anonymized aggregate data**: Cross-tenant benchmarking aggregates that do
  not contain individual-level data.
- **RetentionRunHistory**: Audit records documenting when data was deleted
  (these do not contain personal data beyond a student ID reference, which
  becomes meaningless after erasure).

---

## 6. Tenant-Specific Overrides

Schools and institutes can request customized retention periods via
`TenantRetentionPolicy`:

```csharp
public sealed class TenantRetentionPolicy
{
    public Guid Id { get; set; }
    public string TenantId { get; set; } = "";
    public DateTimeOffset EffectiveFrom { get; set; }
    public DateTimeOffset? EffectiveTo { get; set; }
    public TimeSpan? StudentRecordRetentionOverride { get; set; }
    public TimeSpan? AuditLogRetentionOverride { get; set; }
    public TimeSpan? AnalyticsRetentionOverride { get; set; }
    public TimeSpan? EngagementRetentionOverride { get; set; }
}
```

**Source**: `src/shared/Cena.Infrastructure/Compliance/ComplianceContracts.cs` lines 36--46

The `DefaultRetentionPolicyService` checks for tenant-specific overrides before
falling back to the global defaults in `DataRetentionPolicy`. Override policies
have effective date ranges (`EffectiveFrom` / `EffectiveTo`) to support
time-bounded policy changes.

**Constraint**: Tenant overrides can only shorten retention periods, not extend
them beyond the global defaults, unless the tenant provides legal justification
for longer retention.

---

## 7. Monitoring and Audit

### 7.1 RetentionRunHistory

Every retention worker run creates a `RetentionRunHistory` document:

| Field | Description |
|---|---|
| `Id` | Unique run identifier |
| `RunAt` | UTC timestamp when the run started |
| `CompletedAt` | UTC timestamp when the run completed |
| `Status` | `Running`, `Completed`, or `Failed` |
| `DocumentsScanned` | Total documents evaluated |
| `DocumentsPurged` | Total documents deleted in this run |
| `ErasureRequestsAccelerated` | Erasure requests transitioned past cooling period |
| `ErrorMessage` | Error details if the run failed |
| `CategorySummaries` | Per-category breakdown of expired/purged counts |

### 7.2 Metrics

The retention and erasure workers emit OpenTelemetry-compatible metrics:

| Metric | Type | Description |
|---|---|---|
| `cena.retention.runs_total` | Counter | Total retention worker runs |
| `cena.retention.rows_purged_total` | Counter | Total documents purged across all runs |
| `cena.retention.failures_total` | Counter | Total failed retention runs |
| `cena.retention.duration_seconds` | Histogram | Duration of each retention run |
| `cena.erasure.processed_total` | Counter | Total erasure requests processed |
| `cena.erasure.failed_total` | Counter | Total failed erasure operations |
| `cena.erasure.duration_seconds` | Histogram | Duration of each erasure run |

### 7.3 SIEM logging

All retention and erasure operations emit structured `[SIEM]` log events:

- `RetentionWorker started` -- worker initialization
- `RetentionRunStarted` -- run begins with run ID
- `RetentionRunCompleted_V1` -- run summary with purge counts and duration
- `RetentionRunFailed` -- run failure with error details
- `ErasureWorkerRun` -- erasure run begins
- `ErasureRequestProcessed` -- individual erasure completed
- `ErasureRequestFailed` -- individual erasure failed
- `ErasureRequestAccelerated` -- request transitioned past cooling period
- `ErasureWorkerRunCompleted_V1` -- erasure run summary

---

## 8. Implementation Status

| Requirement | Code Reference | Status |
|---|---|---|
| Retention period constants | `DataRetentionPolicy.cs` | Implemented (5 constants) |
| RetentionWorker background service | `RetentionWorker.cs` | Implemented |
| ErasureWorker background service | `ErasureWorker.cs` | Implemented |
| Audit log purge (5 years) | `RetentionWorker.PurgeAuditLogsAsync()` | Implemented |
| Analytics purge (2 years) | `RetentionWorker.PurgeAnalyticsAsync()` | Implemented |
| Engagement purge (1 year) | `RetentionWorker.PurgeEngagementAsync()` | Implemented |
| Student record archive (7 years) | `RetentionWorker.ArchiveAndPurgeStudentRecordsAsync()` | Implemented |
| Erasure request cooling period (30 days) | `ErasureWorker.GetEligibleErasureRequestsAsync()` | Implemented |
| Erasure request processing | `ErasureWorker.ProcessSingleRequestAsync()` | Implemented |
| Tenant-specific retention overrides | `TenantRetentionPolicy`, `DefaultRetentionPolicyService` | Implemented |
| RetentionRunHistory audit trail | `RetentionRunHistory` document | Implemented |
| OpenTelemetry metrics | `Meter`, `Counter`, `Histogram` in both workers | Implemented |
| SIEM structured logging | `[SIEM]` log events throughout | Implemented |
| Tutor message purge (90 days) | `TutorMessageRetention` constant | Constant defined; purge integration with RetentionWorker pending |
| IP address anonymization (90 days) | -- | Planned |
| Consent record retention (lifetime + 3 years) | -- | Planned (no automated enforcement yet) |
| Compliance admin endpoint | `src/api/Cena.Admin.Api/ComplianceEndpoints.cs` | Implemented (surfaces retention policy via API) |

---

## 9. Code References

| Artifact | File | Description |
|---|---|---|
| Retention policy constants | `src/shared/Cena.Infrastructure/Compliance/DataRetentionPolicy.cs` | `StudentRecordRetention` (7y), `AuditLogRetention` (5y), `AnalyticsRetention` (2y), `EngagementRetention` (1y), `TutorMessageRetention` (90d) |
| Retention worker | `src/shared/Cena.Infrastructure/Compliance/RetentionWorker.cs` | Background service; daily at 2 AM; processes 4 categories + erasure acceleration |
| Erasure worker | `src/shared/Cena.Infrastructure/Compliance/ErasureWorker.cs` | Background service; daily at 3 AM; 30-day cooling period; status: CoolingPeriod -> Processing -> Completed |
| Right to erasure service | `src/shared/Cena.Infrastructure/Compliance/RightToErasureService.cs` | `ErasureRequest`, `ErasureStatus` enum, `IRightToErasureService` |
| Compliance contracts | `src/shared/Cena.Infrastructure/Compliance/ComplianceContracts.cs` | `DataCategory`, `TenantRetentionPolicy`, `RetentionRunHistory`, `RetentionCategorySummary` |
| Compliance endpoints | `src/api/Cena.Admin.Api/ComplianceEndpoints.cs` | Admin API for viewing retention policy and run history |
| Retention worker tests | `src/actors/Cena.Actors.Tests/Compliance/RetentionWorkerTests.cs` | Unit tests for retention enforcement |

---

## 10. Review History

| Date | Version | Reviewer | Changes |
|---|---|---|---|
| 2026-04-13 | 1.0 | claude-code (GD-005) | Initial data retention policy -- DRAFT; derived from codebase analysis of DataRetentionPolicy.cs, RetentionWorker.cs, ErasureWorker.cs, and ComplianceContracts.cs |
