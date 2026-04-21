# Runbook — Redis Session-Store Eviction Spike

**Source task**: prr-020 (pre-release-review 2026-04-20)
**Owner**: Platform SRE
**Severity**: CRITICAL when triggered — eviction of misconception session
  data is a correctness regression, not just a performance blip.
**Alert**: `RedisSessionEvictionRateHigh` in
  `ops/prometheus/alerts-redis-sessions.yml`.

---

## 1. What the alert means

Cena's misconception session store is governed by ADR-0003 — data is
session-scoped, with a 30-day hard cap. The store lives in Redis. If
Redis begins evicting keys because of maxmemory pressure, we silently
lose in-progress session state: the CAS-oracle gate can no longer
reference the student's prior attempts, and the tutor re-asks questions
the student already answered.

`RedisSessionEvictionRateHigh` fires when more than 5% of total keys
are evicted per minute for 2 consecutive minutes. At that rate, an
entire session cohort is purged within ~20 minutes.

## 2. Immediate triage (first 5 minutes)

1. **Check Grafana `redis-session-health` dashboard.**
   - Is `memory_used / memory_max` above 85%? → memory sizing issue.
   - Is `total_keys` growing linearly with no TTL-driven decay? →
     misconception-retention worker may be stalled.
   - Is `hits_total / (hits_total + misses_total)` collapsing? →
     downstream session-correctness already degrading.

2. **Check the misconception retention worker.**
   ```bash
   kubectl logs -n cena -l app=cena-actors-host --tail=200 | \
     grep -i 'MisconceptionRetention'
   ```
   If the worker has not logged a successful pass in the last 6 hours,
   that is the root cause — TTL-based eviction is not running, so
   maxmemory pressure accumulates.

3. **If retention worker is healthy, the root cause is traffic sizing.**
   Decide between the two mitigations below based on how far over 85%
   memory is.

## 3. Mitigation A — scale Redis (memory > 90%)

```bash
# Raise the statefulset's memory limit by 50% as a holding action.
kubectl patch statefulset -n cena cena-redis-sessions \
  --patch '{"spec":{"template":{"spec":{"containers":[{"name":"redis","resources":{"limits":{"memory":"6Gi"},"requests":{"memory":"4Gi"}}}]}}}}'

# Rolling restart — Redis session data survives because misconception
# session scope is bounded by TTL; transient unavailability is a lower
# cost than sustained eviction.
kubectl rollout restart -n cena statefulset/cena-redis-sessions
```

Monitor `RedisSessionMemoryHigh` for 10 minutes post-restart. If it
re-fires, escalate to Mitigation C (shard) before the next peak window.

## 4. Mitigation B — drain the retention worker manually (worker stalled)

```bash
# Kick the MisconceptionRetentionWorker out of a bad state by
# restarting the actor host. The worker is a hosted service; it will
# resume on the next cron tick (default :15 past the hour).
kubectl rollout restart -n cena deployment/cena-actors-host
```

If the worker still doesn't drain keys within 30 minutes, it means
misconception-PII data is not being purged — that's an ADR-0003
violation and a separate P0. File it with the same ticket that
surfaced this alert.

## 5. Mitigation C — shard (sustained >85% memory after scale-up)

Out of scope for this runbook; requires an ADR amendment. File a
P1 task under post-launch with severity=high. Hand the on-call the
Grafana dashboard and this runbook's triage notes.

## 6. Post-incident

- [ ] Add a note to the SLO dashboard: `last_eviction_spike = <date>`.
- [ ] If the root cause was traffic growth, open a task to revisit the
      misconception session TTL (ADR-0003 allows 30 days; shorter TTLs
      reduce memory footprint at the cost of losing longer-running
      sessions).
- [ ] If the root cause was a worker stall, file a task to add a
      liveness probe to `MisconceptionRetentionWorker` specifically.

## 7. 03:00-on-Bagrut-morning failure mode

During a Bagrut exam window, **do not** cold-restart Redis. Prefer
Mitigation B (restart the actor host — stateless) over Mitigation A
(Redis stateful) until the exam window closes. Students who are mid-
session at 03:00 on exam morning would rather see the "staleness
badge" than lose their in-progress session state.
