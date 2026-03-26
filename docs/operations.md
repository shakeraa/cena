# Cena Platform — Operations

> **Status:** Active
> **Last updated:** 2026-03-26
> **Audience:** Engineering team, on-call engineers (founder for first 6 months)

This document covers four operational areas that were identified as gaps in the architecture review: notification throttling, backup and disaster recovery, monitoring and alerting, and CI/CD pipelines.

---

## 1. Notification Throttling (I3)

The Outreach Context subscribes to five trigger types (`StreakExpiring`, `ReviewDue`, `StagnationDetected`, `SessionAbandoned`, `CognitiveLoadCooldownComplete`) and dispatches messages across four channels (WhatsApp, Telegram, push notifications, voice). Without throttling, a student could receive 6+ messages in a single day — destroying trust and triggering opt-outs.

### 1.1 Per-Student Daily Notification Budget

Each student has a configurable daily message budget. Default: **2 messages/day**.

When the budget is exhausted, incoming trigger events are evaluated against a priority queue. Only higher-priority messages displace already-queued lower-priority messages. If all queued messages outrank the new trigger, the new trigger is silently dropped (logged for analytics).

The budget resets at midnight in the student's local timezone.

```
NotificationBudget {
    StudentId:      string
    MaxPerDay:      int        // default: 2, configurable per student
    UsedToday:      int
    QueuedMessages: PriorityQueue<PendingMessage>
    ResetAt:        DateTime   // midnight local time
}
```

### 1.2 Priority Ordering

Messages are ranked by urgency. Lower number = higher priority.

| Priority | Trigger Type | Rationale |
|----------|-------------|-----------|
| 1 | `StreakExpiring` | Time-critical — streak loss is irreversible and emotionally costly |
| 2 | `ReviewDue` | Spaced repetition is time-sensitive — delayed review reduces retention |
| 3 | `StagnationDetected` | Important but not urgent — student is plateauing |
| 4 | `SessionAbandoned` | Re-engagement nudge — lower urgency, student chose to leave |
| 5 | `WeeklySummary` | Informational — can always wait |

When budget is exhausted and a new higher-priority trigger arrives, the lowest-priority queued message is evicted to make room.

### 1.3 Message Deduplication and Merging

When multiple triggers fire within a merge window, they are combined into a single message to reduce noise.

**Merge window:** 1 hour (configurable).

**Merge rules:**

| Trigger A | Trigger B | Merged Message |
|-----------|-----------|----------------|
| `StreakExpiring` | `ReviewDue` | "Your streak expires soon — here's a quick review to keep it alive" |
| `StagnationDetected` | `ReviewDue` | "Feeling stuck? Try this review to break through" |
| `SessionAbandoned` | `ReviewDue` | "Pick up where you left off — a quick review is waiting" |
| `StreakExpiring` | `SessionAbandoned` | "Your streak expires soon — jump back in to save it" |

**Non-mergeable triggers:** `WeeklySummary` is never merged — it is always sent as a standalone digest.

**Merge implementation:** The `OutreachThrottleActor` (child of `StudentActor`) holds a 1-hour buffer. When a trigger arrives:

1. Check if a compatible trigger is already in the buffer.
2. If yes: merge into a single message template, keep the higher priority.
3. If no: add to buffer.
4. After the merge window expires (or on immediate send for priority-1 messages), evaluate against the daily budget and dispatch.

### 1.4 Quiet Hours

No messages are delivered between **22:00 and 07:30** in the student's local timezone (configurable per student).

- Time-critical messages (`StreakExpiring`, priority 1) are **queued for 07:30 delivery**, not dropped.
- All other messages triggered during quiet hours are queued for 07:30 delivery, subject to the daily budget.
- If multiple messages queue during quiet hours, they are evaluated at 07:30 against the daily budget — only the top N (by priority) are sent.

```
QuietHoursConfig {
    StudentId:  string
    Start:      TimeOnly   // default: 22:00
    End:        TimeOnly   // default: 07:30
    Timezone:   string     // from student profile, e.g., "Asia/Jerusalem"
}
```

### 1.5 Channel Cooldown

No second message is sent on the **same channel** within **4 hours**.

If a message is ready to send but the target channel is in cooldown, the system attempts the student's next-preferred channel. If all channels are in cooldown, the message is queued until the earliest cooldown expires.

```
ChannelCooldown {
    Channel:        enum { WhatsApp, Telegram, Push, Voice }
    LastSentAt:     DateTime
    CooldownPeriod: TimeSpan   // default: 4 hours
}
```

### 1.6 Opt-Down Mechanism

Students can reduce notification frequency without fully opting out. Options:

| Setting | Behavior |
|---------|----------|
| **Normal** (default) | 2 messages/day budget |
| **Reduced** | 1 message/day budget |
| **Weekly only** | 3 messages/week, only priority 1-2 triggers + weekly summary |
| **Digest only** | Weekly summary only, all other triggers suppressed |
| **Off** | No messages. Student must re-enable manually. |

Opt-down is surfaced in the mobile app settings and via a "reduce notifications" link in every outbound message. Changing the setting emits a `NotificationPreferenceChanged` domain event on the student aggregate.

### 1.7 Rate Limiting Per Channel

External channel APIs impose their own rate limits. The Outreach service enforces these as a global (not per-student) constraint.

| Channel | Rate Limit | Implementation |
|---------|-----------|----------------|
| **WhatsApp Business API** | 80 messages/second (Meta tier-dependent), 1,000 unique users/24h (initial tier) | Token bucket rate limiter in Outreach service. Tier upgrade requested at 1K MAU. |
| **Telegram Bot API** | 30 messages/second, 1 message/second per chat | Per-chat `SemaphoreSlim` with 1-second delay |
| **Push (FCM/APNs)** | Effectively unlimited for <10K users | No throttle needed at launch |
| **Voice (Twilio)** | 1 concurrent call per number, account-level CPS limits | Queue voice calls, process sequentially per Twilio number |

When a channel rate limit is hit, messages are queued with exponential backoff. The Outreach service logs `channel_rate_limit_hit` metrics for capacity planning.

### 1.8 Message Template Merging Rules

Summary of which trigger types can be combined into a single outbound message:

```
Mergeable pairs (within 1-hour window):
  StreakExpiring  + ReviewDue          → merged
  StreakExpiring  + SessionAbandoned   → merged
  StagnationDetected + ReviewDue      → merged
  SessionAbandoned + ReviewDue        → merged

Never merged:
  WeeklySummary (always standalone)
  Voice calls (always single-purpose)
  Any trigger + CognitiveLoadCooldownComplete (different intent)

Three-way merges:
  Not supported. If three triggers fire within the window, the two
  highest-priority triggers merge; the third is evaluated independently
  against the daily budget.
```

---

## 2. Backup, Disaster Recovery, RTO/RPO (I4)

### 2.1 Per-Data-Store Backup Strategy

| Data Store | RPO | RTO | Backup Method | Recovery Procedure |
|-----------|-----|-----|---------------|-------------------|
| **PostgreSQL/Marten** (event store) | 1 hour | 4 hours | RDS automated snapshots (every 1h) + continuous WAL archiving to S3 | Point-in-time restore from RDS. Replay WAL from S3 to close the gap. Marten projections auto-rebuild from restored event store. |
| **Neo4j AuraDB** (domain graph) | 24 hours | 2 hours | AuraDB built-in daily backups + manual pre-publication snapshot before any curriculum update | Restore from AuraDB backup console. Trigger `RefreshDomainGraphCache` cluster-wide message after restore. |
| **Redis** (hot state) | N/A (cache, reconstructible) | 15 min | No backup needed — Redis is a cache, not a source of truth | Restart ElastiCache. All data rebuilt from PostgreSQL event replay as actors reactivate. |
| **S3** (videos, artifacts) | Near-zero | 1 hour | S3 versioning enabled + cross-region replication to `eu-west-2` (or secondary region) | Access replicated bucket. Update CloudFront origin if primary region is down. |
| **DynamoDB** (cluster discovery) | N/A (ephemeral) | 5 min | No backup — cluster membership is ephemeral | Proto.Actor nodes re-register on startup. DynamoDB table auto-recreated by the cluster provider. |
| **NATS JetStream** | 1 hour | 30 min | Synadia Cloud managed backups with R1 replication | Synadia restores the stream. Consumers resume from last acknowledged position. |

### 2.2 Multi-Region Failover Strategy

**Current posture: Active-passive with manual failover.**

- **Primary region:** `eu-west-1` (or `il-central-1` when available)
- **Passive region:** `eu-west-2` (warm standby for S3 and database backups only)
- **Failover trigger:** Manual decision by founder/on-call after confirming region-wide AWS outage (not a single-service failure)
- **Failover actions:**
  1. Promote RDS read replica in passive region to primary
  2. Deploy ECS services to passive region (pre-staged task definitions in ECR, replicated images)
  3. Update Route 53 DNS to point to passive region load balancer
  4. Restore Neo4j from latest AuraDB backup (or S3 snapshot) in new region
  5. Update NATS connection strings (Synadia Cloud is multi-region)
  6. Notify students via status page

**Estimated manual failover time:** 2-4 hours.

**Upgrade path to active-active (post-product-market-fit):**
- PostgreSQL: migrate to Aurora Global Database with read replicas in secondary region
- Proto.Actor: multi-region cluster with region-affinity actor placement
- NATS: Synadia Cloud multi-region streams
- Trigger: when single-region downtime cost exceeds active-active infrastructure cost (likely at >50K MAU)

### 2.3 Disaster Recovery Runbook — Total Region Failure

**Preconditions:** Primary AWS region is completely unavailable. All services in that region are down.

**Step-by-step:**

1. **Assess (0-15 min):**
   - Confirm region-wide outage via AWS Health Dashboard, not just a single-service issue.
   - Check if Neo4j AuraDB and Synadia Cloud (external managed services) are affected.
   - Decision point: if expected resolution < 1 hour, wait. If > 1 hour or unknown, proceed with failover.

2. **Database failover (15-60 min):**
   - Promote the RDS cross-region read replica to standalone primary in the passive region.
   - Verify Marten can connect and read/write events.
   - Accept RPO of up to 1 hour (WAL data since last replication may be lost).

3. **Compute deployment (30-90 min):**
   - Deploy ECS task definitions in the passive region. Docker images are already replicated in ECR cross-region.
   - Configure Proto.Actor cluster discovery to use DynamoDB in the new region.
   - Deploy Python FastAPI LLM ACL service.
   - Deploy Outreach service.

4. **External services (15-30 min):**
   - Neo4j AuraDB: if unaffected, update connection strings. If affected, restore from S3 domain graph snapshot.
   - NATS JetStream: update Synadia Cloud connection to nearest region endpoint.
   - Redis: deploy new ElastiCache cluster. Accept cold start (actors rebuild from PostgreSQL).

5. **Traffic cutover (5-15 min):**
   - Update Route 53 health-check-based DNS failover to point to the new region's ALB.
   - Invalidate CloudFront caches if CDN origin changed.
   - Mobile app: SignalR reconnects automatically to the new endpoint (DNS-based).

6. **Validation (15-30 min):**
   - Verify actor activation (test student login).
   - Verify event persistence (submit a test exercise).
   - Verify NATS event flow (check Outreach service receives events).
   - Verify LLM calls (test Socratic question generation).

7. **Communication:**
   - Post incident update on status page.
   - If RPO was breached (lost events), identify affected students from the WAL gap and notify them of potential lost progress.

8. **Failback (post-incident):**
   - When the primary region recovers, replicate the passive-region database back to primary.
   - Shift traffic back to primary region during off-peak hours.
   - Conduct post-incident review.

### 2.4 Quarterly DR Drill

Every quarter, the team must execute a simulated DR drill:

1. **Scope:** Full failover to passive region in a staging environment (not production).
2. **Measured outcomes:**
   - Actual RTO achieved vs. target RTO.
   - Data integrity verification (event count comparison, projection consistency).
   - Runbook accuracy — any steps that were missing or wrong are updated.
3. **Documentation:** Each drill produces a report with findings and runbook updates.
4. **First drill:** Within 30 days of production launch.

---

## 3. Monitoring and Alerting (I5)

The founder is the sole on-call engineer for the first 6 months. Alerting must be high-signal, low-noise — every page must be actionable.

### 3.1 Service Level Objectives (SLOs)

| SLO | Target | Error Budget (monthly) | Measurement |
|-----|--------|----------------------|-------------|
| API availability | 99.5% | ~3.6 hours downtime | HTTP 5xx rate on ALB |
| SignalR connection success | 99% | ~7.2 hours of degraded connections | SignalR hub connection success rate |
| LLM response latency (p95) | < 5 seconds | N/A (latency, not availability) | Histogram from LLM ACL service |
| Outreach delivery rate | > 95% | 5% undelivered messages allowed | `messages_sent` / `messages_attempted` in Outreach service |

SLOs are reviewed monthly. If an SLO is consistently exceeded with wide margin, tighten it. If it is consistently missed, investigate root cause before loosening.

### 3.2 Critical Alerts — PagerDuty to Founder's Phone

These alerts indicate imminent or active user impact. Each one pages the founder immediately.

| Alert | Condition | Why It Matters |
|-------|-----------|---------------|
| **Cluster undersize** | Proto.Actor node count < 2 for > 1 minute | Single node = no partition tolerance, one failure away from total outage |
| **DB connection exhaustion** | PostgreSQL connection pool utilization > 80% for > 2 minutes | Approaching the ceiling where new actor activations and event persists start failing |
| **NATS outreach lag** | Consumer lag > 30 seconds on the `cena.outreach.events` stream | Streak reminders and review nudges are being delayed — students may lose streaks |
| **LLM error spike** | LLM ACL error rate > 5% in any 5-minute window (aligned with `docs/llm-routing-strategy.md` Section 7.3 fallback trigger rate) | Students are getting degraded sessions or failures; circuit breaker may be about to open |
| **LLM budget overrun** | Daily LLM spend > 150% of daily budget | Runaway cost — could burn through monthly budget in days |
| **Event store disk** | RDS `FreeStorageSpace` < 20% of allocated | If disk fills, all writes stop — every student is locked out |

### 3.3 Warning Alerts — Slack Channel (`#cena-alerts`)

These alerts indicate trends that need investigation but are not immediately user-impacting.

| Alert | Condition | Investigation |
|-------|-----------|--------------|
| **Onboarding drop-off** | Onboarding completion rate < 60% in any 1-hour window | Check for UX bugs, LLM failures during diagnostic, or slow actor activation |
| **Retention regression** | Day-1 retention drops > 10% week-over-week | Compare cohorts, check notification delivery, review session quality |
| **Actor activation slow** | Actor activation p95 latency > 500ms | Database bottleneck signal — check PostgreSQL connections, snapshot sizes, event replay volume |
| **NATS consumer lag (any)** | Any consumer lag > 5 seconds | Early warning before it becomes critical; investigate consumer health |
| **Redis evictions** | ElastiCache eviction rate > 0 | Cache is undersized — hot state is being dropped, increasing PostgreSQL load |
| **Marten projection lag** | Async projection rebuild lag > 5 minutes | Analytics dashboards are stale — check async daemon health |

### 3.4 Dashboards (Grafana)

Four dashboards, each serving a different audience and purpose.

#### 3.4.1 System Health Dashboard

For: On-call engineer (founder). Glance-able during incidents.

| Panel | Source | Visualization |
|-------|--------|--------------|
| Proto.Actor cluster node count | OpenTelemetry gauge | Single stat with threshold coloring (green >=2, red <2) |
| PostgreSQL active connections | RDS CloudWatch | Gauge with max threshold line |
| PostgreSQL IOPS and latency | RDS CloudWatch | Time series |
| NATS consumer lag per stream | NATS metrics exporter | Time series per consumer |
| LLM ACL error rate | OpenTelemetry counter | Time series with 5% threshold line (aligned with alert at >5%, see Section 3.2) |
| LLM circuit breaker state | OpenTelemetry gauge | State timeline (closed/half-open/open) |
| Redis memory utilization | ElastiCache CloudWatch | Gauge |
| ECS task health | ECS CloudWatch | Status grid per service |

#### 3.4.2 Product Health Dashboard

For: Founder (product decisions). Reviewed daily.

| Panel | Source | Visualization |
|-------|--------|--------------|
| DAU / WAU / MAU | Analytics CQRS projection | Time series with trend line |
| Onboarding completion funnel | Event store projection | Funnel chart (start -> diagnostic -> first session -> profile created) |
| Retention cohorts (D1, D7, D30) | Event store projection | Cohort matrix heatmap |
| Session duration distribution | Event store projection | Histogram |
| Stagnation detection rate | NATS event count | Time series |
| Methodology switch frequency | NATS event count | Time series |

#### 3.4.3 Financial Dashboard

For: Founder (cost management). Reviewed weekly.

| Panel | Source | Visualization |
|-------|--------|--------------|
| LLM spend by model tier (Kimi / Sonnet / Opus) | LLM ACL cost tracking | Stacked area chart |
| LLM cost per active user | LLM ACL + DAU projection | Time series |
| Daily LLM budget utilization | LLM ACL | Gauge with 100% and 150% threshold lines |
| Infrastructure cost (AWS) | AWS Cost Explorer API | Stacked bar chart by service |
| Total cost per active user | All sources | Single stat with trend |

#### 3.4.4 Content Dashboard

For: Curriculum team. Reviewed weekly by the education domain expert, or on demand after a content publication cycle.

| Panel | Source | Visualization |
|-------|--------|--------------|
| Content review queue depth | Content Authoring pipeline | Single stat |
| Publication rate (concepts/week) | Neo4j event log | Time series |
| Question bank coverage (% of concepts with >= 10 exercises) | PostgreSQL exercise bank | Progress bar per subject |
| Pre-generated content status (exercises, hints, videos per concept) | PostgreSQL + S3 | Coverage heatmap |

### 3.5 Observability Stack

```
Application Code (.NET, Python, Node.js)
    │
    ▼
OpenTelemetry SDK (traces, metrics, logs)
    │
    ▼
OpenTelemetry Collector (sidecar or Fargate task)
    │
    ├──► Grafana Cloud (free tier: 10K metrics, 50GB logs, 50GB traces/month)
    │       ├── Grafana dashboards (4 dashboards above)
    │       ├── Grafana Alerting → PagerDuty (critical) / Slack (warning)
    │       └── Tempo (distributed traces)
    │
    └──► AWS CloudWatch (RDS, ElastiCache, ECS native metrics)
            └── CloudWatch Alarms → SNS → PagerDuty integration
```

**Why Grafana Cloud free tier:** At <10K users, the free tier covers all metrics, logs, and traces. Upgrade to paid tier (~$50/month) when volume exceeds free limits. This avoids running self-hosted Grafana/Prometheus on Fargate.

**PagerDuty:** Free tier for 1 user (founder). Critical alerts go through PagerDuty escalation (phone call after 5 minutes if not acknowledged). Warning alerts go to Slack only.

---

## 4. CI/CD Pipelines (I7)

### 4.1 Environment Topology

```
dev (feature branches)
  │
  ▼  PR merge → auto-deploy
staging (main branch)
  │
  ▼  manual promote (one-click GitHub Action)
prod
```

- **dev:** Ephemeral environments per feature branch (optional, for integration testing). Shared dev environment for daily development.
- **staging:** Mirrors production configuration. All migrations and projections run here first. Automated E2E tests gate promotion.
- **prod:** Manual promotion from staging. Blue-green deployment for the actor cluster.

### 4.2 Per-Service Pipelines (GitHub Actions)

#### 4.2.1 .NET Actor Cluster

```yaml
# .github/workflows/actor-cluster.yml
trigger: push to main, PR to main

steps:
  - checkout
  - dotnet restore
  - dotnet build
  - dotnet test (unit + integration tests against PostgreSQL testcontainer)
  - docker build → tag with git SHA
  - docker push → ECR
  - [staging] ECS deploy: update task definition, rolling restart
  - [prod] Blue-green deploy:
      1. Deploy new task definition to green target group
      2. Wait for health checks to pass (5 min)
      3. Shift ALB traffic from blue to green target group
      4. Monitor for 10 min (automated rollback on error rate spike)
      5. Drain blue target group
  - [if Marten schema changed] Trigger Marten projection rebuild:
      - Inline projections: auto-rebuild on next query
      - Async projections: restart async daemon, monitor rebuild progress
```

**Marten projection rebuild step:** The pipeline detects schema changes by comparing the Marten schema hash (stored in a metadata table) between the current and previous deployment. If changed, the async daemon is restarted with a rebuild flag. Inline projections rebuild automatically.

#### 4.2.2 Python FastAPI (LLM ACL)

```yaml
# .github/workflows/llm-acl.yml
trigger: push to main (changes in /services/llm-acl/**)

steps:
  - checkout
  - pip install -r requirements.txt
  - pytest (unit tests + mocked LLM responses)
  - docker build → tag with git SHA
  - docker push → ECR
  - [staging] App Runner auto-deploy (triggered by ECR image push)
  - [prod] App Runner auto-deploy with traffic pause:
      1. Push image to prod ECR
      2. App Runner detects new image, deploys new revision
      3. Automatic traffic shift (App Runner handles rolling)
      4. Monitor error rate for 5 min
```

#### 4.2.3 Remotion Worker

```yaml
# .github/workflows/remotion-worker.yml
trigger: push to main (changes in /services/remotion-worker/**)

steps:
  - checkout
  - npm ci
  - npm run build
  - docker build → tag with git SHA
  - docker push → ECR
  - [staging/prod] Update Fargate task definition
  - No rolling deploy needed — Remotion worker runs as batch tasks,
    not a long-running service. New tasks pick up the new image.
```

#### 4.2.4 React Native Mobile App

```yaml
# .github/workflows/mobile.yml
trigger: push to main (changes in /apps/mobile/**)

steps:
  - checkout
  - npm ci
  - jest (unit tests)
  - [staging] Detox E2E tests on iOS and Android simulators
  - Fastlane:
      - iOS: build → upload to TestFlight
      - Android: build → upload to Play Console internal testing track
  - [prod] Manual promotion:
      - Founder reviews TestFlight/Play Console build
      - One-click GitHub Action dispatches Fastlane promote
      - iOS: submit to App Store Review (24-48 hour review cycle)
      - Android: promote to production track (typically <2 hours)
```

**App Store review buffer:** Plan deployments requiring mobile changes with 48-hour lead time for iOS review.

#### 4.2.5 React PWA (Web App)

```yaml
# .github/workflows/web.yml
trigger: push to main (changes in /apps/web/**)

steps:
  - checkout
  - npm ci
  - npm run build
  - npm test (unit + component tests)
  - [staging] Deploy to staging S3 bucket + CloudFront invalidation
  - [prod] Deploy to production S3 bucket + CloudFront invalidation
  - Verify: curl health check on CloudFront URL
```

### 4.3 Database Migration Strategy

| Store | Migration Approach | CI/CD Integration |
|-------|-------------------|-------------------|
| **PostgreSQL/Marten** (event store tables) | Marten auto-migration on startup. Event store schema changes are append-only (new event types). | No explicit migration step. Marten's `AutoCreateSchemaObjects` handles it. Staging validates before prod. |
| **PostgreSQL/Marten** (projections) | Inline projections auto-rebuild on schema change. Async projections managed by Marten async daemon. | Pipeline detects projection schema changes and triggers daemon rebuild in staging. |
| **Neo4j** (domain graph) | Graph updates go through the Content Authoring pipeline, not CI/CD. Curriculum team publishes via authoring tools. | Not part of CI/CD. `CurriculumPublished` event triggers in-memory cache refresh. |
| **DynamoDB** (cluster discovery) | Table auto-created by Proto.Actor's DynamoDB cluster provider. | No migration. Table is ephemeral. |

### 4.4 Knowledge Graph Hot-Reload

Content updates do **not** require a deployment:

1. Curriculum team publishes changes via the Content Authoring tool.
2. Authoring tool writes to Neo4j AuraDB.
3. Authoring tool emits a `CurriculumPublished` event to NATS JetStream.
4. Each Proto.Actor node subscribes to this event and refreshes its in-memory domain graph cache.
5. Students immediately see updated content — zero downtime, zero deployment.

### 4.5 Hotfix Process

**Backend services (actor cluster, LLM ACL, Outreach):**
1. Create a `hotfix/*` branch from `main`.
2. Fix, test locally, push.
3. Pipeline deploys directly to staging for verification.
4. Manual promotion to prod (same blue-green or App Runner flow).
5. Merge hotfix branch back to `main`.

**Mobile app:**
- **JS-only changes:** Deploy via EAS Updates or Bitrise CodePush (React Native OTA updates). Microsoft App Center CodePush was retired March 31, 2025; use a supported alternative. Bypasses App Store review. Available to users within minutes.
- **Native changes (new native modules, SDK updates):** Full App Store submission required. Use expedited review if available.

**Web app:**
- Direct deploy to prod S3 + CloudFront invalidation. Available globally within minutes.

### 4.6 Rollback Strategy

| Service | Rollback Method | Time to Rollback |
|---------|----------------|-----------------|
| .NET Actor Cluster | Shift ALB traffic back to blue target group | < 1 minute |
| Python FastAPI | App Runner: revert to previous revision | < 2 minutes |
| Remotion Worker | Update task definition to previous image tag | Next task uses old image |
| React Native | EAS Updates / Bitrise CodePush rollback (JS) or App Store revert (native) | Minutes (JS) / 24-48h (native) |
| React PWA | Redeploy previous build from S3 versioning | < 5 minutes |

---

## Appendix: Related Documents

- `docs/architecture-design.md` — System architecture and technology choices
- `docs/failure-modes.md` — Proto.Actor cluster failure mode analysis
- `docs/architecture-audit.md` — Architecture review and gap analysis
- `docs/llm-routing-strategy.md` — LLM model routing and cost analysis
- `docs/event-schemas.md` — Domain event schema definitions
