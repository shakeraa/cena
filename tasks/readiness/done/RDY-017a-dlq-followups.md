# RDY-017a: DLQ Follow-ups (Replay Tool Testing + Health Check Integration Test)

- **Priority**: Low — core DLQ functionality is complete (RDY-017)
- **Complexity**: Junior engineer
- **Source**: RDY-017 review gaps
- **Tier**: 3 (post-pilot polish)
- **Effort**: 1-2 days

## Parent: RDY-017 (NATS Dead-Letter Queue Stream + TLS)

RDY-017 is complete against all 7 acceptance criteria. These are quality polish items identified during review.

## Sub-tasks

### 1. Validate replay script against live NATS

`scripts/nats/nats-dlq-replay.sh` uses `nats stream get` which may not match the exact `nats` CLI version deployed. Test against a running NATS JetStream instance:

- [ ] Verify `nats stream get CENA_DLQ --count N --raw` returns expected JSON format
- [ ] Verify replay re-publishes to original subject correctly
- [ ] Test `--filter` and `--dry-run` flags
- [ ] Document required `nats` CLI version in script header

### 2. Integration test for NatsDlqHealthCheck

`NatsDlqHealthCheck` uses raw SQL (`SELECT COUNT(*) FROM mt_doc_natsoutboxdeadletter`). Add an integration test that:

- [ ] Seeds a `NatsOutboxDeadLetter` document via Marten
- [ ] Verifies health check returns Healthy when count < threshold
- [ ] Seeds 50+ documents and verifies Degraded result
- [ ] Verifies `dlq_depth` data key in health result

### 3. DLQ depth in admin dashboard UI

The health probe reports DLQ depth but the admin Vue dashboard doesn't display it yet:

- [ ] Add DLQ section to admin system health page
- [ ] Show current depth, threshold, and status (healthy/degraded)
- [ ] Link to replay instructions

## Files

- `scripts/nats/nats-dlq-replay.sh` — test + fix against live NATS
- New: `src/shared/Cena.Infrastructure.Tests/Health/NatsDlqHealthCheckTests.cs`
- `src/admin/full-version/src/pages/apps/system/health.vue` — DLQ section (if page exists)
