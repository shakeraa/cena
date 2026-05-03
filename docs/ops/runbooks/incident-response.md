# Incident Response Runbook — Cena Platform (DRAFT)

> **🚨 First-draft runbook for ops + legal review 🚨**
>
> Engineering baseline. Legal counsel owns notification obligations + timing.
> SRE owns operational runbook correctness.

## Purpose

When something goes wrong in production — privacy breach, security
incident, significant data loss, extended outage — this runbook tells
on-call what to do, in what order, with which clocks ticking.

## Severity matrix

| SEV | Criteria | Response time | Examples |
|---|---|---|---|
| SEV-1 | Student data exfiltration confirmed; prod auth bypass; entire platform down > 15 min during school hours | **< 15 min on-call ack** | DB dump leaked; auth bypass CVE; data-tier offline |
| SEV-2 | Partial outage; PII exposure to authenticated-but-unauthorised users; individual student data visible to wrong classroom | **< 30 min on-call ack** | IDOR bug; cross-school data leak via a specific admin endpoint |
| SEV-3 | Degraded functionality; no PII exposure; error rate spike < 5% | **< 2 hr on-call ack** | Admin dashboard partially broken; a specific question type fails |
| SEV-4 | Non-urgent bug; isolated report | **Next business day** | Visual glitch; single-user flow issue |

Escalation: SEV-2 unresolved in 4 hours → escalate to SEV-1.

## Roles

| Role | Responsibility |
|---|---|
| **On-call engineer** | First responder. Triages severity, executes containment, escalates. |
| **Incident commander (IC)** | Senior engineer. Declared for any SEV-1/2. Coordinates, communicates, owns the incident timeline. |
| **Scribe** | Documents timeline in Slack + postmortem doc. Non-responder role; anyone not actively fixing. |
| **Privacy officer / DPO** | Owns regulatory-notification decisions. Paged on any incident involving personal data. |
| **Legal counsel** | Owns external legal communications. Paged on SEV-1, optional on SEV-2. |
| **Engineering leadership** | Approves customer-facing comms + public disclosure. |

## Stage 1 — Detection + triage (minutes 0–15)

**On-call engineer**:

1. Acknowledge the page within the SEV SLA.
2. Open a dedicated Slack channel `#incident-YYYYMMDD-short-slug`.
3. Assess severity from the matrix above. **If unsure, treat as one
   level higher** — downgrade is easier than upgrade.
4. For SEV-1/2: page the IC + DPO.
5. Post a 1-line summary in the channel:
   `SEV-N | <what's broken> | <what's currently unknown>`
6. Start the timeline (see §Timeline template).

## Stage 2 — Containment (0–60 min for SEV-1/2)

Goal: **stop the bleeding**, not root-cause.

Containment actions in priority order:

1. **Isolate attack vector** (if security incident):
   - Revoke compromised tokens via Firebase admin SDK
   - Rotate secrets (Postgres password, NATS creds, Firebase admin key)
   - Block compromised IPs at ingress
2. **Stop data flow** (if exfiltration suspected):
   - Take the affected endpoint(s) offline via feature flag
   - Disconnect the compromised service from NATS bus (prevent further
     event emission)
3. **Preserve evidence**:
   - Snapshot affected DB before any cleanup
   - Export relevant NATS subject replays
   - Do NOT `kubectl delete pod` on anything suspicious without
     taking a `kubectl logs --previous` dump first

## Stage 3 — Investigation (minutes 60+)

Only after containment:

1. Enable verbose logging on the affected path.
2. Pull OTel traces for the incident window.
3. Query Serilog sinks (`structured_logs` index) for relevant
   correlation IDs.
4. Query the audit-event stream (`AuditEventDocument` Marten doc) for
   any administrative actions during the window.
5. Hypothesise root cause. Document in timeline.

## Stage 4 — Notification obligations (owned by DPO / Legal)

**Critical time windows** (counsel to confirm):

| Obligation | Who | When | Authority |
|---|---|---|---|
| Internal-only (dev team) | IC | ASAP | n/a |
| Notify DPO | On-call | At incident declaration | Internal |
| Notify senior leadership | IC | Within 2h of SEV-1/2 confirmation | Internal |
| UK ICO notification (data breach) | DPO | **72h** of becoming aware | UK GDPR Art. 33 |
| EU supervisory authority | DPO | **72h** of becoming aware | GDPR Art. 33 |
| Israeli Privacy Protection Authority | DPO | **As soon as possible** | Privacy Protection Law Amendment 2024 § 46 |
| Data-subject notification | DPO | "Without undue delay" | UK GDPR Art. 34 |
| FTC notification (US students, COPPA) | Legal | **Per 2025 rule amendments** — counsel to confirm exact trigger + window | COPPA Rule 16 C.F.R. 312 |
| Parents of affected minors | Legal + Product | Per COPPA + applicable state laws | FTC + state AG guidance |
| Cyber insurance notice | Legal | Per policy terms (typically ≤24h of awareness) | Policy |

**Drafted notifications** live in `docs/legal/notifications/` (to be
created by Legal). Template variables: incident-id, date-discovered,
date-occurred, affected-subject-count, data-types-involved,
containment-status, mitigation-summary.

## Stage 5 — Resolution + verification

Before declaring resolved:

1. Root cause identified + documented.
2. Fix deployed + verified in production.
3. Metrics back to baseline for 30+ minutes.
4. No open follow-up-required in the incident channel.
5. Scribe has written the postmortem draft.

## Stage 6 — Post-incident (48h)

- **Postmortem** posted to `docs/postmortems/YYYY-MM-DD-slug.md`
  within 48 hours of resolution. Blameless format. Template at
  `docs/postmortems/TEMPLATE.md` (to be created).
- **Action items** filed as RDY tasks with owners + due dates.
- **Regression tests** added where feasible.
- **Runbook updates** applied if any step here needed clarification.

## Timeline template

Pin this in the incident Slack channel from minute 0:

```
## Incident INC-YYYYMMDD-slug — SEV-N

**Started**: 2026-04-19 14:23 UTC (detection)
**Declared**: 2026-04-19 14:26 UTC (on-call page ack)
**Contained**: ☐ not yet / 2026-04-19 HH:MM UTC
**Resolved**: ☐ not yet / 2026-04-19 HH:MM UTC

### Timeline
- 14:23  First alert: <what fired>
- 14:26  On-call ack (name)
- 14:32  IC declared (name)
- ...

### Decisions
- ...

### Open questions
- ...
```

## Contacts (to be filled by ops)

| Role | Primary | Backup | Pager |
|---|---|---|---|
| On-call | TBD | TBD | TBD |
| IC (senior) | TBD | TBD | TBD |
| DPO | TBD | TBD | TBD |
| Legal counsel | TBD | TBD | TBD |
| Engineering leadership | TBD | TBD | TBD |
| Infra provider support | TBD | TBD | TBD |

## Tabletop exercise cadence

- **Pre-pilot**: One end-to-end tabletop before any external student
  onboards. Scenario: "A student's free-text self-assessment appears
  in another student's tutor context." Focus: SEV, containment,
  notification workflow.
- **Quarterly** post-pilot: Rotate scenarios (DB exfil, auth bypass,
  third-party provider breach, accidental public repo push).

## What counsel + ops must fill in

- [ ] Exact notification-window times per regime (§Stage 4)
- [ ] Pre-approved external-comms templates (parent email, press
  statement, ICO notification)
- [ ] Contact roster (§Contacts table)
- [ ] Insurance-policy incident-reporting clause summary
- [ ] Affected-subject-notification template email (age-appropriate
  language for a minor's parent)
- [ ] Regulator contact points + preferred channel (email vs portal)

## Related

- Privacy policy: `docs/legal/privacy-policy.md`
- Children's privacy policy: `docs/legal/privacy-policy-children.md`
- COPPA compliance statement: `docs/legal/coppa-compliance-statement-draft.md`
- DPA (Anthropic): `docs/legal/dpa-anthropic-draft.md`
- ADR-0003 (data scope): `docs/adr/0003-misconception-session-scope.md`
- ADR-0037 (affective data): `docs/adr/0037-affective-signal-in-pedagogy.md`

---

**Status**: DRAFT — ops + legal review required.
**Last touched**: 2026-04-19 (engineering draft)
